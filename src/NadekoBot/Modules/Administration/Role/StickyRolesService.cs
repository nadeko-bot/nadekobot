#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Common.ModuleBehaviors;
using System.Net;

namespace NadekoBot.Modules.Administration;

public sealed class StickyRolesService : INService, IReadyExecutor
{
    private static readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan _retention = TimeSpan.FromDays(30);

    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;
    private readonly DbService _db;
    private ConcurrentHashSet<ulong> _stickyRoles = new();

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
            _stickyRoles = new(await ctx
                                  .Set<GuildConfig>()
                                  .ToLinqToDBTable()
                                  .Where(Queries.GuildOnShard<GuildConfig>(x => x.GuildId,
                                      _creds.TotalShards,
                                      _client.ShardId))
                                  .Where(x => x.StickyRoles)
                                  .Select(x => x.GuildId)
                                  .ToListAsync());
        }

        _client.UserJoined += ClientOnUserJoined;
        _client.UserLeft += ClientOnUserLeft;

        if (_client.ShardId == 0)
            _ = Task.Run(CleanupLoopInternalAsync);
    }

    private async Task CleanupLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(_cleanupInterval);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                await using var ctx = _db.GetDbContext();
                await ctx.GetTable<StickyRole>()
                         .Where(x => x.DateAdded < DateTime.UtcNow - _retention)
                         .DeleteAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Sticky roles: cleanup failed");
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
        var roleIds = string.Join(',',
            guRoles.Where(x => !x.IsEveryone && !x.IsManaged).Select(x => x.Id.ToString()));

        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<StickyRole>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         UserId = userId,
                         RoleIds = roleIds,
                         DateAdded = DateTime.UtcNow
                     },
                     _ => new()
                     {
                         RoleIds = roleIds,
                         DateAdded = DateTime.UtcNow
                     },
                     () => new()
                     {
                         GuildId = guildId,
                         UserId = userId
                     });
    }

    private Task ClientOnUserJoined(SocketGuildUser user)
    {
        if (!_stickyRoles.Contains(user.Guild.Id))
            return Task.CompletedTask;
        
        _ = Task.Run(() => RestoreRolesInternalAsync(user));

        return Task.CompletedTask;
    }

    private async Task RestoreRolesInternalAsync(SocketGuildUser user)
    {
        var savedIds = await GetRolesAsync(user.Guild.Id, user.Id);
        if (savedIds.Length == 0)
            return;

        // AddRolesAsync sends a single PATCH, so one unusable role would drop all of them.
        var toAdd = GetAssignableRoles(savedIds, user.Guild, user.Guild.CurrentUser.Hierarchy);
        if (toAdd.Count == 0)
            return;

        try
        {
            await user.AddRolesAsync(toAdd);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            Log.Warning("Sticky roles: missing role management permissions in {GuildId}", user.Guild.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Sticky roles: failed restoring roles for {UserId} in {GuildId}", user.Id, user.Guild.Id);
        }
    }

    public static List<IRole> GetAssignableRoles(ulong[] savedIds, IGuild guild, int botHierarchy)
    {
        var toAdd = new List<IRole>(savedIds.Length);
        foreach (var id in savedIds)
        {
            var role = guild.GetRole(id);
            if (role is null || role.Id == guild.EveryoneRole.Id || role.IsManaged || role.Position >= botHierarchy)
                continue;

            toAdd.Add(role);
        }

        return toAdd;
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
            _stickyRoles.TryRemove(guildId);
        }

        return config.StickyRoles;
    }
}