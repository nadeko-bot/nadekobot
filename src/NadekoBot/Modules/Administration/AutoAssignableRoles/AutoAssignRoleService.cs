#nullable disable
using NadekoBot.Db.Models;
using System.Net;
using System.Threading.Channels;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Administration.Services;

public sealed class AutoAssignRoleService : INService, IReadyExecutor
{
    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly ShardData _shardData;

    //guildid/roleid
    private ConcurrentDictionary<ulong, IReadOnlyList<ulong>> _autoAssignableRoles;

    private readonly Channel<SocketGuildUser> _assignQueue = Channel.CreateBounded<SocketGuildUser>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public AutoAssignRoleService(
        DiscordSocketClient client,
        IBot bot,
        DbService db,
        ShardData shardData)
    {
        _client = client;
        _shardData = shardData;
        _db = db;
    }

    public async Task OnReadyAsync()
    {
        await using (var uow = _db.GetDbContext())
        {
            _autoAssignableRoles = await uow.GetTable<GuildConfig>()
                                            .Where(x => Queries.GuildOnShard(x.GuildId,
                                                _shardData.TotalShards,
                                                _shardData.ShardId))
                                            .Where(x => x.AutoAssignRoleIds != null)
                                            .ToListAsyncLinqToDB()
                                            .Fmap(x => x
                                                       .ToDictionary<GuildConfig, ulong, IReadOnlyList<ulong>>(
                                                           k => k.GuildId,
                                                           v => v.GetAutoAssignableRoles())
                                                       .ToConcurrent());
        }

        _client.UserJoined += OnClientOnUserJoined;
        _client.RoleDeleted += OnClientRoleDeleted;
        
        while (true)
        {
            var user = await _assignQueue.Reader.ReadAsync();
            if (!_autoAssignableRoles.TryGetValue(user.Guild.Id, out var savedRoleIds))
                continue;

            try
            {
                var roleIds = savedRoleIds.Select(roleId => user.Guild.GetRole(roleId))
                                          .Where(x => x is not null)
                                          .ToList();

                if (roleIds.Any())
                {
                    await user.AddRolesAsync(roleIds);
                    await Task.Delay(250);
                }
                else
                {
                    Log.Warning(
                        "Disabled 'Auto assign role' feature on {GuildName} [{GuildId}] server the roles dont exist",
                        user.Guild.Name,
                        user.Guild.Id);

                    await DisableAarAsync(user.Guild.Id);
                }
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                Log.Warning(
                    "Disabled 'Auto assign role' feature on {GuildName} [{GuildId}] server because I don't have role management permissions",
                    user.Guild.Name,
                    user.Guild.Id);

                await DisableAarAsync(user.Guild.Id);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in aar. Probably one of the roles doesn't exist");
            }
        }
    }

    private async Task OnClientRoleDeleted(SocketRole role)
    {
        if (_autoAssignableRoles.TryGetValue(role.Guild.Id, out var roles) && roles.Contains(role.Id))
            await ToggleAarAsync(role.Guild.Id, role.Id);
    }

    private async Task OnClientOnUserJoined(SocketGuildUser user)
    {
        if (_autoAssignableRoles.TryGetValue(user.Guild.Id, out _))
            await _assignQueue.Writer.WriteAsync(user);
    }

    public async Task<IReadOnlyList<ulong>> ToggleAarAsync(ulong guildId, ulong roleId)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.GuildConfigsForId(guildId, set => set);
        var roles = gc.GetAutoAssignableRoles();
        if (!roles.Remove(roleId) && roles.Count < 3)
            roles.Add(roleId);

        gc.SetAutoAssignableRoles(roles);
        await uow.SaveChangesAsync();

        if (roles.Count > 0)
            _autoAssignableRoles[guildId] = roles;
        else
            _autoAssignableRoles.TryRemove(guildId, out _);

        return roles;
    }

    public async Task DisableAarAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();

        await uow.Set<GuildConfig>()
                 .AsNoTracking()
                 .Where(x => x.GuildId == guildId)
                 .UpdateAsync(_ => new()
                 {
                     AutoAssignRoleIds = null
                 });

        _autoAssignableRoles.TryRemove(guildId, out _);

        await uow.SaveChangesAsync();
    }

    public async Task SetAarRolesAsync(ulong guildId, IEnumerable<ulong> newRoles)
    {
        await using var uow = _db.GetDbContext();

        var gc = uow.GuildConfigsForId(guildId, set => set);
        gc.SetAutoAssignableRoles(newRoles);

        await uow.SaveChangesAsync();
    }

    public bool TryGetRoles(ulong guildId, out IReadOnlyList<ulong> roles)
        => _autoAssignableRoles.TryGetValue(guildId, out roles);
}

public static class GuildConfigExtensions
{
    public static List<ulong> GetAutoAssignableRoles(this GuildConfig gc)
    {
        if (string.IsNullOrWhiteSpace(gc.AutoAssignRoleIds))
            return new();

        return gc.AutoAssignRoleIds.Split(',').Select(ulong.Parse).ToList();
    }

    public static void SetAutoAssignableRoles(this GuildConfig gc, IEnumerable<ulong> roles)
        => gc.AutoAssignRoleIds = roles.Join(',');
}