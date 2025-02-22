using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration.Services;

public class AutoPublishService : IExecNoCommand, IReadyExecutor, INService
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly IBotCredsProvider _creds;
    private ConcurrentDictionary<ulong, ulong> _enabled = new();

    public AutoPublishService(DbService db, DiscordSocketClient client, IBotCredsProvider creds)
    {
        _db = db;
        _client = client;
        _creds = creds;
    }

    public async Task ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (guild is null)
            return;

        if (msg.Channel.GetChannelType() != ChannelType.News)
            return;

        if (!_enabled.TryGetValue(guild.Id, out var cid) || cid != msg.Channel.Id)
            return;

        await msg.CrosspostAsync(new RequestOptions()
        {
            RetryMode = RetryMode.AlwaysFail
        });
    }

    public async Task OnReadyAsync()
    {
        var creds = _creds.GetCreds();

        await using var ctx = _db.GetDbContext();
        var items = await ctx.GetTable<AutoPublishChannel>()
                             .Where(x => Queries.GuildOnShard(x.GuildId, creds.TotalShards, _client.ShardId))
                             .ToListAsyncLinqToDB();

        _enabled = items
                   .ToDictionary(x => x.GuildId, x => x.ChannelId)
                   .ToConcurrent();
        
        _client.LeftGuild += ClientOnLeftGuild;
    }

    private async Task ClientOnLeftGuild(SocketGuild guild)
    {
        await using var ctx = _db.GetDbContext();
        _enabled.TryRemove(guild.Id, out _);

        await ctx.GetTable<AutoPublishChannel>()
                 .Where(x => x.GuildId == guild.Id)
                 .DeleteAsync();
    }

    public async Task<bool> ToggleAutoPublish(ulong guildId, ulong channelId)
    {
        await using var ctx = _db.GetDbContext();
        var deleted = await ctx.GetTable<AutoPublishChannel>()
                               .DeleteAsync(x => x.GuildId == guildId && x.ChannelId == channelId);

        if (deleted != 0)
        {
            _enabled.TryRemove(guildId, out _);
            return false;
        }

        await ctx.GetTable<AutoPublishChannel>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         ChannelId = channelId,
                         DateAdded = DateTime.UtcNow,
                     },
                     old => new()
                     {
                         ChannelId = channelId,
                         DateAdded = DateTime.UtcNow,
                     },
                     () => new()
                     {
                         GuildId = guildId
                     });

        _enabled[guildId] = channelId;

        return true;
    }
}