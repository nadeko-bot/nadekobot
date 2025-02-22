#nullable disable
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration.Services;

public class VcRoleService : INService, IReadyExecutor
{
    public ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, IRole>> VcRoles { get; }
    public ConcurrentDictionary<ulong, System.Collections.Concurrent.ConcurrentQueue<(bool, IGuildUser, IRole)>> ToAssign { get; }
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly IBot _bot;
    private readonly ShardData _shardData;

    public VcRoleService(DiscordSocketClient client, IBot bot, DbService db, ShardData shardData)
    {
        _db = db;
        _client = client;
        _bot = bot;
        _shardData = shardData;

        VcRoles = new();
        ToAssign = new();
    }

    public async Task OnReadyAsync()
    {
        IEnumerable<IGrouping<ulong, VcRoleInfo>> vcRoles;
        using (var uow = _db.GetDbContext())
        {
            vcRoles = await uow.GetTable<VcRoleInfo>()
               .AsQueryable()
               .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
               .ToListAsync()
               .Fmap(x => x.GroupBy(x => x.GuildId));
        }

        await vcRoles.Select(x => InitializeVcRole(x.Key, x.ToList())).WhenAll();


        _client.UserVoiceStateUpdated += ClientOnUserVoiceStateUpdated;

        while (true)
        {
            Task Selector(System.Collections.Concurrent.ConcurrentQueue<(bool, IGuildUser, IRole)> queue)
            {
                return Task.Run(async () =>
                {
                    while (queue.TryDequeue(out var item))
                    {
                        var (add, user, role) = item;

                        try
                        {
                            if (add)
                            {
                                if (!user.RoleIds.Contains(role.Id))
                                    await user.AddRoleAsync(role);
                            }
                            else
                            {
                                if (user.RoleIds.Contains(role.Id))
                                    await user.RemoveRoleAsync(role);
                            }
                        }
                        catch
                        {
                        }

                        await Task.Delay(250);
                    }
                });
            }

            await ToAssign.Values.Select(Selector).Append(Task.Delay(1000)).WhenAll();
        }
    }

    private async Task InitializeVcRole(ulong guildId, IReadOnlyList<VcRoleInfo> confs)
    {
        var g = _client.GetGuild(guildId);
        if (g is null)
            return;

        var infos = new ConcurrentDictionary<ulong, IRole>();
        var missingRoles = new List<VcRoleInfo>();
        VcRoles.AddOrUpdate(guildId, infos, delegate
        { return infos; });
        foreach (var ri in confs)
        {
            var role = g.GetRole(ri.RoleId);
            if (role is null)
            {
                missingRoles.Add(ri);
                continue;
            }

            infos.TryAdd(ri.VoiceChannelId, role);
        }

        if (missingRoles.Any())
        {
            await using var uow = _db.GetDbContext();
            uow.RemoveRange(missingRoles);
            await uow.SaveChangesAsync();

            Log.Warning("Removed {MissingRoleCount} missing roles from {ServiceName}",
                missingRoles.Count,
                nameof(VcRoleService));
        }
    }

    public void AddVcRole(ulong guildId, IRole role, ulong vcId)
    {
        ArgumentNullException.ThrowIfNull(role);

        var guildVcRoles = VcRoles.GetOrAdd(guildId, new ConcurrentDictionary<ulong, IRole>());

        guildVcRoles.AddOrUpdate(vcId, role, (_, _) => role);
        using var uow = _db.GetDbContext();
        var toDelete = uow.Set<VcRoleInfo>()
                       .FirstOrDefault(x => x.VoiceChannelId == vcId); // remove old one
        if (toDelete is not null)
            uow.Remove(toDelete);
        uow.Set<VcRoleInfo>().Add(new()
        {
            VoiceChannelId = vcId,
            RoleId = role.Id
        }); // add new one
        uow.SaveChanges();
    }

    public bool RemoveVcRole(ulong guildId, ulong vcId)
    {
        if (!VcRoles.TryGetValue(guildId, out var guildVcRoles))
            return false;

        if (!guildVcRoles.TryRemove(vcId, out _))
            return false;

        using var uow = _db.GetDbContext();
        var toRemove = uow.Set<VcRoleInfo>().Where(x => x.VoiceChannelId == vcId).ToList();
        uow.RemoveRange(toRemove);
        uow.SaveChanges();

        return true;
    }

    private Task ClientOnUserVoiceStateUpdated(SocketUser usr, SocketVoiceState oldState, SocketVoiceState newState)
    {
        if (usr is not SocketGuildUser gusr)
            return Task.CompletedTask;

        var oldVc = oldState.VoiceChannel;
        var newVc = newState.VoiceChannel;
        _ = Task.Run(() =>
        {
            try
            {
                if (oldVc != newVc)
                {
                    ulong guildId;
                    guildId = newVc?.Guild.Id ?? oldVc.Guild.Id;

                    if (VcRoles.TryGetValue(guildId, out var guildVcRoles))
                    {
                        //remove old
                        if (oldVc is not null && guildVcRoles.TryGetValue(oldVc.Id, out var role))
                            Assign(false, gusr, role);
                        //add new
                        if (newVc is not null && guildVcRoles.TryGetValue(newVc.Id, out role))
                            Assign(true, gusr, role);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in VcRoleService VoiceStateUpdate");
            }
        });
        return Task.CompletedTask;
    }

    private void Assign(bool v, SocketGuildUser gusr, IRole role)
    {
        var queue = ToAssign.GetOrAdd(gusr.Guild.Id, new System.Collections.Concurrent.ConcurrentQueue<(bool, IGuildUser, IRole)>());
        queue.Enqueue((v, gusr, role));
    }
}