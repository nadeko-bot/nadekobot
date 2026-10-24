using System.Net;
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

    public async ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (guild is not SocketGuild sg)
            return;

        if (!_enabled.TryGetValue(guild.Id, out var cid) || cid != msg.Channel.Id)
            return;

        if (msg.Channel is not IGuildChannel channel || channel.GetChannelType() != ChannelType.News)
            return;

        // discord needs manage messages to publish a message of someone else
        var perms = sg.CurrentUser.GetPermissions(channel);
        if (!(msg.Author.Id == sg.CurrentUser.Id ? perms.SendMessages : perms.ManageMessages))
        {
            await DisableInternalAsync(guild.Id, channel.Id);
            return;
        }

        try
        {
            await msg.CrosspostAsync(new RequestOptions()
            {
                RetryMode = RetryMode.AlwaysFail
            });
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            await DisableInternalAsync(guild.Id, channel.Id);
        }
        catch (RateLimitedException)
        {
            // discord allows only a few publishes per hour in one channel
        }
    }

    private async Task DisableInternalAsync(ulong guildId, ulong channelId)
    {
        // only the caller which removes the entry logs and deletes, so parallel messages log once
        if (!_enabled.TryRemove(guildId, out _))
            return;

        Log.Warning("Auto-publish turned off in {GuildId}, the bot can not publish in {ChannelId}", guildId, channelId);
        await DeleteInternalAsync(guildId);
    }

    private async Task DeleteInternalAsync(ulong guildId)
    {
        _enabled.TryRemove(guildId, out _);

        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<AutoPublishChannel>()
                 .Where(x => x.GuildId == guildId)
                 .DeleteAsync();
    }

    public async Task OnReadyAsync()
    {
        var creds = _creds.GetCreds();

        await using var ctx = _db.GetDbContext();
        var items = await ctx.GetTable<AutoPublishChannel>()
                             .Where(Queries.GuildOnShard<AutoPublishChannel>(x => x.GuildId, creds.TotalShards, _client.ShardId))
                             .ToListAsyncLinqToDB();

        _enabled = items
                   .ToDictionary(x => x.GuildId, x => x.ChannelId)
                   .ToConcurrent();
        
        _client.LeftGuild += ClientOnLeftGuild;
    }

    private Task ClientOnLeftGuild(SocketGuild guild)
        => DeleteInternalAsync(guild.Id);

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