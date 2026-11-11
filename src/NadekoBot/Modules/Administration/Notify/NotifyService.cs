using System.Net;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
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

    // one warning for each server and event until a post works again, so a busy event does not flood the log
    private readonly ConcurrentDictionary<(ulong GuildId, NotifyType Type), byte> _noPermsLogged = new();

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

    private void RegisterModels()
    {
        RegisterModel<LevelUpNotifyModel>();
        RegisterModel<ProtectionNotifyModel>();
        RegisterModel<AddRoleRewardNotifyModel>();
        RegisterModel<RemoveRoleRewardNotifyModel>();
        RegisterModel<NiceCatchNotifyModel>();
        RegisterModel<FishCurrencyNotifyModel>();
        RegisterModel<HoneypotNotifyModel>();
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();
        _events = (await uow.GetTable<Notify>()
                .Where(Queries.GuildOnShard<Notify>(x => x.GuildId,
                    _creds.TotalShards,
                    _client.ShardId))
                .ToListAsyncLinqToDB())
            .GroupBy(x => x.Type)
            .ToDictionary(x => x.Key, x => x.ToDictionary(x => x.GuildId).ToConcurrent())
            .ToConcurrent();

        RegisterModels();
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
                _ = Task.Run(() => OnEvent(data));
                return;
            }

            await _pubSub.Pub(data.GetTypedKey(), data);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Unknown error occurred while trying to trigger {NotifyEvent} for {NotifyModel}",
                T.KeyName,
                data);
        }
    }

    private async Task OnEvent<T>(T model)
        where T : struct, INotifyModel<T>
    {
        if (!_events.TryGetValue(T.NotifyType, out var subs))
            return;

        // make sure the event is consumed
        // only in the guild it was meant for
        if (model.TryGetGuildId(out var gid))
        {
            if (!subs.TryGetValue(gid, out var conf))
                return;

            try
            {
                await HandleNotifyEvent(conf, model);
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "Error occured while sending notification {NotifyEvent} to guild {GuildId}",
                    T.NotifyType,
                    gid);
            }

            return;
        }

        // todo optimize this
        foreach (var key in subs.Keys)
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

    private async Task HandleNotifyEvent<T>(Notify conf, T model)
        where T : struct, INotifyModel<T>
    {
        var guild = _client.GetGuild(conf.GuildId);
        var logKey = (conf.GuildId, T.NotifyType);

        // bot probably left the guild, cleanup?
        if (guild is null)
        {
            _noPermsLogged.TryRemove(logKey, out _);
            return;
        }

        IMessageChannel? channel;
        // if notify channel is specified for this event, send the event to that channel
        if (conf.ChannelId is ulong confCid)
        {
            channel = guild.GetTextChannel(confCid);
        }
        else
        {
            // otherwise get the origin channel of the event
            if (!model.TryGetChannelId(out var cid))
                return;

            channel = guild.GetChannel(cid) as IMessageChannel;
        }

        if (channel is null)
            return;

        if (!CanPost(guild, channel))
        {
            if (_noPermsLogged.TryAdd(logKey, 0))
            {
                Log.Warning("The bot can not post the {NotifyEvent} notification in channel {ChannelId} of guild {GuildId}: "
                            + "it needs View Channel, Send Messages (Send Messages in Threads in a thread) and Embed Links",
                    T.NotifyType,
                    channel.Id,
                    conf.GuildId);
            }

            return;
        }

        IUser? user = null;
        if (model.TryGetUserId(out var userId))
        {
            try
            {
                user = guild.GetUser(userId)
                       ?? await _client.GetUserAsync(userId);
            }
            catch
            {
                user = null;
            }
        }

        var rctx = new ReplacementContext(guild: guild, channel: channel, user: user);

        var template = SmartText.CreateFrom(conf.Message);
        var st = template;
        foreach (var modelRep in T.GetReplacements())
        {
            rctx.WithOverride(GetPhToken(modelRep.Name), () => modelRep.Func(model, guild));
        }

        st = await _repSvc.ReplaceAsync(st, rctx);
        var response = st is SmartPlainText spt
            ? _mss.Response(channel).Confirm(spt.Text)
            : _mss.Response(channel).Text(st).Mentions(TemplateMentions.FromTemplate(template));

        try
        {
            await response.SendAsync();
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            if (_noPermsLogged.TryAdd(logKey, 0))
            {
                Log.Warning(ex,
                    "Discord refused the {NotifyEvent} notification in channel {ChannelId} of guild {GuildId}",
                    T.NotifyType,
                    channel.Id,
                    conf.GuildId);
            }

            return;
        }

        _noPermsLogged.TryRemove(logKey, out _);
    }

    private static bool CanPost(SocketGuild guild, IMessageChannel channel)
    {
        if (channel is not SocketGuildChannel gch)
            return false;

        // a thread has no overwrites of its own, it uses the parent channel's
        var isThread = gch is SocketThreadChannel;
        var permsChannel = gch is SocketThreadChannel { ParentChannel: { } parent } ? parent : gch;
        var perms = guild.CurrentUser.GetPermissions(permsChannel);
        var canSend = isThread ? perms.SendMessagesInThreads : perms.SendMessages;

        return perms.ViewChannel && canSend && perms.EmbedLinks;
    }

    private static string GetPhToken(string name)
        => $"%event.{name}%";

    public async Task<bool> EnableAsync(
        ulong guildId,
        ulong? channelId,
        NotifyType nType,
        string message)
    {
        // check if the notify type model supports null channel
        if (channelId is null)
        {
            var model = GetRegisteredModel(nType);
            if (!model.SupportsOriginTarget)
                return false;
        }


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

        _noPermsLogged.TryRemove((guildId, nType), out _);
        return true;
    }

    public async Task DisableAsync(ulong guildId, NotifyType nType)
    {
        await using var uow = _db.GetDbContext();
        var deleted = await uow.GetTable<Notify>()
            .Where(x => x.GuildId == guildId && x.Type == nType)
            .DeleteAsync();

        if (deleted == 0)
            return;

        _noPermsLogged.TryRemove((guildId, nType), out _);

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
        var data = new NotifyModelData(T.NotifyType,
            T.SupportsOriginTarget,
            T.GetReplacements().Map(x => x.Name));

        _models[T.NotifyType] = data;

        _pubSub.Sub<T>(new(T.KeyName), async (data) => await OnEvent(data));
    }

    public NotifyModelData GetRegisteredModel(NotifyType nType)
        => _models[nType];
}