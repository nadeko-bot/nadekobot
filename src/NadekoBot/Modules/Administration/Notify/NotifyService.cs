using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Generators;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.Modules.Administration;

public sealed class NotifyService : IReadyExecutor, INotifySubscriber, INService
{
    private readonly DbService _db;
    private readonly IMessageSenderService _mss;
    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;
    private readonly IReplacementService _repSvc;
    private readonly IPubSub _pubSub;
    private ConcurrentDictionary<NotifyType, ConcurrentDictionary<ulong, Notify>> _events = new();

    public NotifyService(
        DbService db,
        IMessageSenderService mss,
        DiscordSocketClient client,
        IBotCreds creds,
        IReplacementService repSvc,
        IPubSub pubSub)
    {
        _db = db;
        _mss = mss;
        _client = client;
        _creds = creds;
        _repSvc = repSvc;
        _pubSub = pubSub;
    }

    private void RegisterModels() {

        RegisterModel<LevelUpNotifyModel>();
        RegisterModel<ProtectionNotifyModel>();
        RegisterModel<AddRoleRewardNotifyModel>();
        RegisterModel<RemoveRoleRewardNotifyModel>();

    }

    public async Task OnReadyAsync()
    {
        RegisterModels();

        await using var uow = _db.GetDbContext();
        _events = (await uow.GetTable<Notify>()
                .Where(x => Queries.GuildOnShard(x.GuildId,
                    _creds.TotalShards,
                    _client.ShardId))
                .ToListAsyncLinqToDB())
            .GroupBy(x => x.Type)
            .ToDictionary(x => x.Key, x => x.ToDictionary(x => x.GuildId).ToConcurrent())
            .ToConcurrent();


        await SubscribeToEvent<LevelUpNotifyModel>();
    }

    private async Task SubscribeToEvent<T>()
        where T : struct, INotifyModel<T>
    {
        await _pubSub.Sub(new TypedKey<T>(T.KeyName), async (model) => await OnEvent(model));
    }

    public async Task NotifyAsync<T>(T data, bool isShardLocal = false)
        where T : struct, INotifyModel<T>
    {
        try
        {
            if (isShardLocal)
            {
                await OnEvent(data);
                return;
            }

            await _pubSub.Pub(data.GetTypedKey(), data);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Unknown error occurred while trying to triger {NotifyEvent} for {NotifyModel}",
                T.KeyName,
                data);
        }
    }

    private async Task OnEvent<T>(T model)
        where T : struct, INotifyModel<T>
    {
        if (_events.TryGetValue(T.NotifyType, out var subs))
        {
            if (model.TryGetGuildId(out var gid))
            {
                if (!subs.TryGetValue(gid, out var conf))
                    return;

                await HandleNotifyEvent(conf, model);
                return;
            }

            foreach (var key in subs.Keys.ToArray())
            {
                if (subs.TryGetValue(key, out var notif))
                {
                    try
                    {
                        await HandleNotifyEvent(notif, model);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex,
                            "Error occured while sending notification {NotifyEvent} to guild {GuildId}: {ErrorMessage}",
                            T.NotifyType,
                            key,
                            ex.Message);
                    }

                    await Task.Delay(500);
                }
            }
        }
    }

    private async Task HandleNotifyEvent<T>(Notify conf, T model)
        where T: struct, INotifyModel<T>
    {
        var guild = _client.GetGuild(conf.GuildId);
        var channel = guild?.GetTextChannel(conf.ChannelId);

        if (guild is null || channel is null)
            return;

        IUser? user = null;
        if (model.TryGetUserId(out var userId))
        {
            user = guild.GetUser(userId) ?? _client.GetUser(userId);
        }

        var rctx = new ReplacementContext(guild: guild, channel: channel, user: user);

        var st = SmartText.CreateFrom(conf.Message);
        foreach (var modelRep in T.GetReplacements())
        {
            rctx.WithOverride(GetPhToken(modelRep.Name), () => modelRep.Func(model, guild));
        }

        st = await _repSvc.ReplaceAsync(st, rctx);
        if (st is SmartPlainText spt)
        {
            await _mss.Response(channel)
                .Confirm(spt.Text)
                .SendAsync();
            return;
        }

        await _mss.Response(channel)
            .Text(st)
            .Sanitize(false)
            .SendAsync();
    }

    private static string GetPhToken(string name) => $"%event.{name}%";

    public async Task EnableAsync(
        ulong guildId,
        ulong channelId,
        NotifyType nType,
        string message)
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<Notify>()
            .InsertOrUpdateAsync(() => new()
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    Type = nType,
                    Message = message,
                },
                (_) => new()
                {
                    Message = message,
                    ChannelId = channelId
                },
                () => new()
                {
                    GuildId = guildId,
                    Type = nType
                });

        var eventDict = _events.GetOrAdd(nType, _ => new());
        eventDict[guildId] = new()
        {
            GuildId = guildId,
            ChannelId = channelId,
            Type = nType,
            Message = message
        };
    }

    public async Task DisableAsync(ulong guildId, NotifyType nType)
    {
        await using var uow = _db.GetDbContext();
        var deleted = await uow.GetTable<Notify>()
            .Where(x => x.GuildId == guildId && x.Type == nType)
            .DeleteAsync();

        if (deleted == 0)
            return;

        if (!_events.TryGetValue(nType, out var guildsDict))
            return;

        guildsDict.TryRemove(guildId, out _);
    }

    public async Task<IReadOnlyCollection<Notify>> GetForGuildAsync(ulong guildId, int page = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);

        await using var ctx = _db.GetDbContext();
        var list = await ctx.GetTable<Notify>()
            .Where(x => x.GuildId == guildId)
            .OrderBy(x => x.Type)
            .Skip(page * 10)
            .Take(10)
            .ToListAsyncLinqToDB();

        return list;
    }

    public async Task<Notify?> GetNotifyAsync(ulong guildId, NotifyType nType)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<Notify>()
            .Where(x => x.GuildId == guildId && x.Type == nType)
            .FirstOrDefaultAsyncLinqToDB();
    }

    // messed up big time, it was supposed to be fully extensible, but it's stored as an enum in the database already...
    private readonly ConcurrentDictionary<NotifyType, NotifyModelData> _models = new();
    public void RegisterModel<T>() where T : struct, INotifyModel<T>
    {
        var data = new NotifyModelData(T.NotifyType, T.GetReplacements().Map(x => x.Name));
        _models[T.NotifyType] = data;
    }

    public NotifyModelData GetRegisteredModel(NotifyType nType) => _models[nType];
}
