using System.Diagnostics.CodeAnalysis;
using NadekoBot.Db.Models;
using System.Net;
using System.Threading.Channels;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Administration.Services;

public enum AarToggleResult
{
    Added,
    Removed,
    Disabled,
    LimitReached
}

public sealed class AutoAssignRoleService(
    DiscordSocketClient client,
    DbService db,
    ShardData shardData) : INService, IReadyExecutor
{
    public const int MAX_ROLES = 3;

    // joins are queued for the whole shard, so a burst in one big server must not push out other servers' members
    private const int QUEUE_CAPACITY = 1_000;
    private static readonly TimeSpan _assignDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _reconnectWait = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<ulong, IReadOnlyList<ulong>> _autoAssignableRoles = new();

    private readonly Channel<PendingJoin> _assignQueue = Channel.CreateBounded<PendingJoin>(
        new BoundedChannelOptions(QUEUE_CAPACITY)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public async Task OnReadyAsync()
    {
        await using (var uow = db.GetDbContext())
        {
            var configs = await uow.GetTable<GuildConfig>()
                .Where(Queries.GuildOnShard<GuildConfig>(x => x.GuildId,
                    shardData.TotalShards,
                    shardData.ShardId))
                .Where(x => x.AutoAssignRoleIds != null && x.AutoAssignRoleIds != "")
                .Select(x => new { x.GuildId, x.AutoAssignRoleIds })
                .ToListAsyncLinqToDB();

            foreach (var gc in configs)
            {
                var roles = GuildConfigExtensions.ParseRoleIds(gc.AutoAssignRoleIds);
                if (roles.Count > 0)
                    _autoAssignableRoles[gc.GuildId] = roles;
            }
        }

        client.UserJoined += OnClientOnUserJoined;
        client.RoleDeleted += OnClientRoleDeleted;

        _ = Task.Run(AssignLoopInternalAsync);
    }

    private async Task AssignLoopInternalAsync()
    {
        await foreach (var join in _assignQueue.Reader.ReadAllAsync())
        {
            // while the shard reconnects the guild cache is incomplete, and a missing role would disable the feature
            while (client.ConnectionState != ConnectionState.Connected)
                await Task.Delay(_reconnectWait);

            try
            {
                await AssignInternalAsync(join);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in aar. Probably one of the roles doesn't exist");
            }
        }
    }

    private async Task AssignInternalAsync(PendingJoin join)
    {
        if (!_autoAssignableRoles.TryGetValue(join.GuildId, out var savedRoleIds))
            return;

        var guild = client.GetGuild(join.GuildId);
        // the member may have left while waiting in the queue
        var user = guild?.GetUser(join.UserId);
        if (guild is null || user is null || !guild.IsConnected)
            return;

        var roles = new List<IRole>(savedRoleIds.Count);
        foreach (var roleId in savedRoleIds)
        {
            if (guild.GetRole(roleId) is { } role)
                roles.Add(role);
        }

        if (roles.Count == 0)
        {
            Log.Warning(
                "Disabled 'Auto assign role' feature on {GuildName} [{GuildId}] server the roles dont exist",
                guild.Name,
                guild.Id);

            await DisableAarAsync(guild.Id);
            return;
        }

        try
        {
            await user.AddRolesAsync(roles);
            await Task.Delay(_assignDelay);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            Log.Warning(
                "Disabled 'Auto assign role' feature on {GuildName} [{GuildId}] server because I don't have role management permissions",
                guild.Name,
                guild.Id);

            await DisableAarAsync(guild.Id);
        }
    }

    private async Task OnClientRoleDeleted(SocketRole role)
    {
        if (_autoAssignableRoles.TryGetValue(role.Guild.Id, out var roles) && roles.Contains(role.Id))
            await RemoveAarRoleAsync(role.Guild.Id, role.Id);
    }

    private Task OnClientOnUserJoined(SocketGuildUser user)
    {
        if (_autoAssignableRoles.ContainsKey(user.Guild.Id))
            _assignQueue.Writer.TryWrite(new(user.Guild.Id, user.Id));

        return Task.CompletedTask;
    }

    public async Task<(AarToggleResult Result, IReadOnlyList<ulong> Roles)> ToggleAarAsync(ulong guildId, ulong roleId)
    {
        await using var uow = db.GetDbContext();
        var gc = uow.GuildConfigsForId(guildId, set => set);
        var roles = gc.GetAutoAssignableRoles();

        AarToggleResult result;
        if (roles.Remove(roleId))
            result = roles.Count == 0 ? AarToggleResult.Disabled : AarToggleResult.Removed;
        else if (roles.Count >= MAX_ROLES)
            return (AarToggleResult.LimitReached, roles);
        else
        {
            roles.Add(roleId);
            result = AarToggleResult.Added;
        }

        gc.SetAutoAssignableRoles(roles);
        await uow.SaveChangesAsync();

        UpdateCache(guildId, roles);
        return (result, roles);
    }

    private async Task RemoveAarRoleAsync(ulong guildId, ulong roleId)
    {
        await using var uow = db.GetDbContext();
        var gc = uow.GuildConfigsForId(guildId, set => set);
        var roles = gc.GetAutoAssignableRoles();
        if (!roles.Remove(roleId))
            return;

        gc.SetAutoAssignableRoles(roles);
        await uow.SaveChangesAsync();

        UpdateCache(guildId, roles);
    }

    public async Task DisableAarAsync(ulong guildId)
    {
        await using var uow = db.GetDbContext();

        await uow.GetTable<GuildConfig>()
            .Where(x => x.GuildId == guildId)
            .Set(x => x.AutoAssignRoleIds, (string?)null)
            .UpdateAsync();

        _autoAssignableRoles.TryRemove(guildId, out _);
    }

    public async Task SetAarRolesAsync(ulong guildId, IReadOnlyList<ulong> newRoles)
    {
        await using var uow = db.GetDbContext();

        var gc = uow.GuildConfigsForId(guildId, set => set);
        gc.SetAutoAssignableRoles(newRoles);

        await uow.SaveChangesAsync();

        UpdateCache(guildId, newRoles);
    }

    public bool TryGetRoles(ulong guildId, [NotNullWhen(true)] out IReadOnlyList<ulong>? roles)
        => _autoAssignableRoles.TryGetValue(guildId, out roles);

    private void UpdateCache(ulong guildId, IReadOnlyList<ulong> roles)
    {
        if (roles.Count > 0)
            _autoAssignableRoles[guildId] = roles;
        else
            _autoAssignableRoles.TryRemove(guildId, out _);
    }

    private readonly record struct PendingJoin(ulong GuildId, ulong UserId);
}

public static class GuildConfigExtensions
{
    public static List<ulong> GetAutoAssignableRoles(this GuildConfig gc)
        => ParseRoleIds(gc.AutoAssignRoleIds);

    public static List<ulong> ParseRoleIds(string? roleIds)
    {
        if (string.IsNullOrWhiteSpace(roleIds))
            return [];

        var result = new List<ulong>(AutoAssignRoleService.MAX_ROLES);
        foreach (var range in roleIds.AsSpan().Split(','))
        {
            if (ulong.TryParse(roleIds.AsSpan(range).Trim(), out var id))
                result.Add(id);
        }

        return result;
    }

    public static void SetAutoAssignableRoles(this GuildConfig gc, IEnumerable<ulong> roles)
        => gc.AutoAssignRoleIds = roles.Join(',');
}
