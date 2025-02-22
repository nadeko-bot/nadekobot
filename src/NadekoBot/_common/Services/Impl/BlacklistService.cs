using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Collections.Frozen;

namespace NadekoBot.Modules.Permissions.Services;

public sealed class BlacklistService : IExecOnMessage, IReadyExecutor, INService
{
    public int Priority
        => int.MaxValue;

    private readonly DbService _db;
    private readonly IPubSub _pubSub;
    private readonly IBotCreds _creds;
    private readonly DiscordSocketClient _client;

    private FrozenSet<ulong> blacklistedGuilds = new HashSet<ulong>().ToFrozenSet();
    private FrozenSet<ulong> blacklistedUsers = new HashSet<ulong>().ToFrozenSet();
    private FrozenSet<ulong> blacklistedChannels = new HashSet<ulong>().ToFrozenSet();

    private readonly TypedKey<bool> _blPubKey = new("blacklist.reload");

    public BlacklistService(
        DbService db,
        IPubSub pubSub,
        IBotCreds creds,
        DiscordSocketClient client)
    {
        _db = db;
        _pubSub = pubSub;
        _creds = creds;
        _client = client;

        _pubSub.Sub(_blPubKey, async _ => await Reload(false));
    }

    public async Task OnReadyAsync()
    {
        _client.JoinedGuild += async (g) =>
        {
            if (blacklistedGuilds.Contains(g.Id))
            {
                await g.LeaveAsync();
            }
        };

        await Reload(false);
    }

    private ValueTask OnReload(BlacklistEntry[] newBlacklist)
    {
        newBlacklist ??= [];

        blacklistedGuilds =
            new HashSet<ulong>(newBlacklist.Where(x => x.Type == BlacklistType.Server).Select(x => x.ItemId))
                .ToFrozenSet();
        blacklistedChannels =
            new HashSet<ulong>(newBlacklist.Where(x => x.Type == BlacklistType.Channel).Select(x => x.ItemId))
                .ToFrozenSet();
        blacklistedUsers =
            new HashSet<ulong>(newBlacklist.Where(x => x.Type == BlacklistType.User).Select(x => x.ItemId))
                .ToFrozenSet();

        return default;
    }

    public Task<bool> ExecOnMessageAsync(IGuild? guild, IUserMessage usrMsg)
    {
        if (guild is not null && blacklistedGuilds.Contains(guild.Id))
        {
            Log.Information("Blocked input from blacklisted guild: {GuildName} [{GuildId}]",
                guild.Name,
                guild.Id.ToString());
            return Task.FromResult(true);
        }

        if (blacklistedChannels.Contains(usrMsg.Channel.Id))
        {
            Log.Information("Blocked input from blacklisted channel: {ChannelName} [{ChannelId}]",
                usrMsg.Channel.Name,
                usrMsg.Channel.Id.ToString());
        }


        if (blacklistedUsers.Contains(usrMsg.Author.Id))
        {
            Log.Information("Blocked input from blacklisted user: {UserName} [{UserId}]",
                usrMsg.Author.ToString(),
                usrMsg.Author.Id.ToString());
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public async Task<IReadOnlyList<BlacklistEntry>> GetBlacklist(BlacklistType type)
    {
        await using var uow = _db.GetDbContext();

        return await uow
                     .GetTable<BlacklistEntry>()
                     .Where(x => x.Type == type)
                     .ToListAsync();
    }

    public async Task Reload(bool publish = true)
    {
        var totalShards = _creds.TotalShards;
        await using var uow = _db.GetDbContext();
        var items = uow.GetTable<BlacklistEntry>()
                       .Where(x => x.Type != BlacklistType.Server
                                   || (x.Type == BlacklistType.Server
                                       && Queries.GuildOnShard(x.ItemId, totalShards, _client.ShardId)))
                       .ToArray();


        if (publish)
        {
            await _pubSub.Pub(_blPubKey, true);
        }

        await OnReload(items);
    }

    public async Task Blacklist(BlacklistType type, ulong id)
    {
        if (_creds.OwnerIds.Contains(id))
            return;

        await using var uow = _db.GetDbContext();

        await uow
              .GetTable<BlacklistEntry>()
              .InsertAsync(() => new()
              {
                  ItemId = id,
                  Type = type,
              });

        if (type == BlacklistType.User)
        {
            await uow.GetTable<DiscordUser>()
                     .Where(x => x.UserId == id)
                     .UpdateAsync(_ => new()
                     {
                         CurrencyAmount = 0
                     });
        }

        await Reload();
    }

    public async Task UnBlacklist(BlacklistType type, ulong id)
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<BlacklistEntry>()
                 .Where(bi => bi.ItemId == id && bi.Type == type)
                 .DeleteAsync();

        await Reload();
    }

    public async Task BlacklistUsers(IReadOnlyCollection<ulong> toBlacklist)
    {
        await using var uow = _db.GetDbContext();
        var bc = uow.GetTable<BlacklistEntry>();
        await bc.BulkCopyAsync(toBlacklist.Select(uid => new BlacklistEntry
        {
            ItemId = uid,
            Type = BlacklistType.User
        }));

        var blList = toBlacklist.ToList();
        await uow.GetTable<DiscordUser>()
                 .Where(x => blList.Contains(x.UserId))
                 .UpdateAsync(_ => new()
                 {
                     CurrencyAmount = 0
                 });

        await Reload();
    }
}