#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Administration;

public sealed class StickyRolesService : INService, IReadyExecutor
{
    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;
    private readonly DbService _db;
    private HashSet<ulong> _stickyRoles = new();

    public StickyRolesService(
        DiscordSocketClient client,
        IBotCreds creds,
        DbService db)
    {
        _client = client;
        _creds = creds;
        _db = db;
    }


    public async Task OnReadyAsync()
    {
        await using (var ctx = _db.GetDbContext())
        {
            _stickyRoles = (await ctx
                                  .Set<GuildConfig>()
                                  .ToLinqToDBTable()
                                  .Where(x => Queries.GuildOnShard(x.GuildId,
                                      _creds.TotalShards,
                                      _client.ShardId))
                                  .Where(x => x.StickyRoles)
                                  .Select(x => x.GuildId)
                                  .ToListAsync())
                .ToHashSet();
        }

        _client.UserJoined += ClientOnUserJoined;
        _client.UserLeft += ClientOnUserLeft;

        // cleanup old ones every hour
        // 30 days retention
        if (_client.ShardId == 0)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync())
            {
                await using var ctx = _db.GetDbContext();
                await ctx.GetTable<StickyRole>()
                         .Where(x => x.DateAdded < DateTime.UtcNow - TimeSpan.FromDays(30))
                         .DeleteAsync();
            }
        }
    }

    private Task ClientOnUserLeft(SocketGuild guild, SocketUser user)
    {
        if (user is not SocketGuildUser gu)
            return Task.CompletedTask;

        if (!_stickyRoles.Contains(guild.Id))
            return Task.CompletedTask;

        _ = Task.Run(async () => await SaveRolesAsync(guild.Id, gu.Id, gu.Roles));

        return Task.CompletedTask;
    }

    private async Task SaveRolesAsync(ulong guildId, ulong userId, IReadOnlyCollection<SocketRole> guRoles)
    {
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<StickyRole>()
                 .InsertAsync(() => new()
                 {
                     GuildId = guildId,
                     UserId = userId,
                     RoleIds = string.Join(',',
                         guRoles.Where(x => !x.IsEveryone && !x.IsManaged).Select(x => x.Id.ToString())),
                     DateAdded = DateTime.UtcNow
                 });
    }

    private Task ClientOnUserJoined(SocketGuildUser user)
    {
        if (!_stickyRoles.Contains(user.Guild.Id))
            return Task.CompletedTask;
        
        _ = Task.Run(async () =>
        {
            var roles = await GetRolesAsync(user.Guild.Id, user.Id);

            await user.AddRolesAsync(roles);
        });

        return Task.CompletedTask;
    }

    private async Task<ulong[]> GetRolesAsync(ulong guildId, ulong userId)
    {
        await using var ctx = _db.GetDbContext();
        var stickyRolesEntry = await ctx
                                     .GetTable<StickyRole>()
                                     .Where(x => x.GuildId == guildId && x.UserId == userId)
                                     .DeleteWithOutputAsync();

        if (stickyRolesEntry is { Length: > 0 })
        {
            return stickyRolesEntry[0].GetRoleIds();
        }

        return [];
    }

    public async Task<bool> ToggleStickyRoles(ulong guildId, bool? newState = null)
    {
        await using var ctx = _db.GetDbContext();
        var config = ctx.GuildConfigsForId(guildId, set => set);

        config.StickyRoles = newState ?? !config.StickyRoles;
        await ctx.SaveChangesAsync();

        if (config.StickyRoles)
        {
            _stickyRoles.Add(guildId);
        }
        else
        {
            _stickyRoles.Remove(guildId);
        }

        return config.StickyRoles;
    }
}