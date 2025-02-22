using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Xp.Services;
using OneOf;
using OneOf.Types;
using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;

namespace NadekoBot.Modules.Administration.Services;

public class SelfAssignedRolesService : INService, IReadyExecutor
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;

    private ConcurrentHashSet<ulong> _sarAds = new();

    public SelfAssignedRolesService(DbService db, DiscordSocketClient client, IBotCreds creds)
    {
        _db = db;
        _client = client;
        _creds = creds;
    }

    public async Task AddAsync(ulong guildId, ulong roleId, int groupNumber)
    {
        await using var ctx = _db.GetDbContext();

        await ctx.GetTable<SarGroup>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         GroupNumber = groupNumber,
                         IsExclusive = false
                     },
                     _ => new()
                     {
                     },
                     () => new()
                     {
                         GuildId = guildId,
                         GroupNumber = groupNumber
                     });

        await ctx.GetTable<Sar>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         RoleId = roleId,
                         LevelReq = 0,
                         GuildId = guildId,
                         SarGroupId = ctx.GetTable<SarGroup>()
                                         .Where(x => x.GuildId == guildId && x.GroupNumber == groupNumber)
                                         .Select(x => x.Id)
                                         .First()
                     },
                     _ => new()
                     {
                         SarGroupId = ctx.GetTable<SarGroup>()
                                         .Where(x => x.GuildId == guildId && x.GroupNumber == groupNumber)
                                         .Select(x => x.Id)
                                         .First()
                     },
                     () => new()
                     {
                         RoleId = roleId,
                         GuildId = guildId,
                     });
    }

    public async Task<bool> RemoveAsync(ulong guildId, ulong roleId)
    {
        await using var ctx = _db.GetDbContext();

        var deleted = await ctx.GetTable<Sar>()
                               .Where(x => x.RoleId == roleId && x.GuildId == guildId)
                               .DeleteAsync();

        return deleted > 0;
    }

    public async Task<bool> SetGroupNameAsync(ulong guildId, int groupNumber, string? name)
    {
        await using var ctx = _db.GetDbContext();

        var changes = await ctx.GetTable<SarGroup>()
                               .Where(x => x.GuildId == guildId && x.GroupNumber == groupNumber)
                               .UpdateAsync(x => new()
                               {
                                   Name = name
                               });

        return changes > 0;
    }

    public async Task<IReadOnlyCollection<SarGroup>> GetSarsAsync(ulong guildId)
    {
        await using var ctx = _db.GetDbContext();

        var sgs = await ctx.GetTable<SarGroup>()
                           .Where(x => x.GuildId == guildId)
                           .LoadWith(x => x.Roles)
                           .ToListAsyncLinqToDB();

        return sgs;
    }

    public async Task<bool> SetRoleLevelReq(ulong guildId, ulong roleId, int levelReq)
    {
        await using var ctx = _db.GetDbContext();
        var changes = await ctx.GetTable<Sar>()
                               .Where(x => x.GuildId == guildId && x.RoleId == roleId)
                               .UpdateAsync(_ => new()
                               {
                                   LevelReq = levelReq,
                               });

        return changes > 0;
    }

    public async Task<bool> SetGroupRoleReq(ulong guildId, int groupNumber, ulong? roleId)
    {
        await using var ctx = _db.GetDbContext();
        var changes = await ctx.GetTable<SarGroup>()
                               .Where(x => x.GuildId == guildId && x.GroupNumber == groupNumber)
                               .UpdateAsync(_ => new()
                               {
                                   RoleReq = roleId
                               });

        return changes > 0;
    }

    public async Task<bool?> SetGroupExclusivityAsync(ulong guildId, int groupNumber)
    {
        await using var ctx = _db.GetDbContext();
        var changes = await ctx.GetTable<SarGroup>()
                               .Where(x => x.GuildId == guildId && x.GroupNumber == groupNumber)
                               .UpdateWithOutputAsync(old => new()
                                   {
                                       IsExclusive = !old.IsExclusive
                                   },
                                   (o, n) => n.IsExclusive);

        if (changes.Length == 0)
        {
            return null;
        }

        return changes[0];
    }

    public async Task<SarGroup?> GetRoleGroup(ulong guildId, ulong roleId)
    {
        await using var ctx = _db.GetDbContext();

        var group = await ctx.GetTable<SarGroup>()
                             .Where(x => x.GuildId == guildId && x.Roles.Any(x => x.RoleId == roleId))
                             .LoadWith(x => x.Roles)
                             .FirstOrDefaultAsyncLinqToDB();


        return group;
    }

    public async Task<bool> DeleteRoleGroup(ulong guildId, int groupNumber)
    {
        await using var ctx = _db.GetDbContext();

        var deleted = await ctx.GetTable<SarGroup>()
                               .Where(x => x.GuildId == guildId && x.GroupNumber == groupNumber)
                               .DeleteAsync();

        return deleted > 0;
    }

    public async Task<bool> ToggleAutoDelete(ulong guildId)
    {
        await using var ctx = _db.GetDbContext();

        var delted = await ctx.GetTable<SarAutoDelete>()
                              .DeleteAsync(x => x.GuildId == guildId);

        if (delted > 0)
        {
            _sarAds.TryRemove(guildId);
            return false;
        }

        await ctx.GetTable<SarAutoDelete>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         IsEnabled = true,
                         GuildId = guildId,
                     },
                     (_) => new()
                     {
                         IsEnabled = true
                     },
                     () => new()
                     {
                         GuildId = guildId
                     });

        _sarAds.Add(guildId);
        return true;
    }

    public bool GetAutoDelete(ulong guildId)
        => _sarAds.Contains(guildId);

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();
        var guilds = await uow.GetTable<SarAutoDelete>()
                              .Where(x => x.IsEnabled
                                          && Queries.GuildOnShard(x.GuildId, _creds.TotalShards, _client.ShardId))
                              .Select(x => x.GuildId)
                              .ToListAsyncLinqToDB();

        _sarAds = new(guilds);
    }
}

public sealed class SarAssignerService : INService, IReadyExecutor
{
    private readonly XpService _xp;
    private readonly DbService _db;

    private readonly Channel<SarAssignerDataItem> _channel =
        Channel.CreateBounded<SarAssignerDataItem>(100);


    public SarAssignerService(XpService xp, DbService db)
    {
        _xp = xp;
        _db = db;
    }

    public async Task OnReadyAsync()
    {
        var reader = _channel.Reader;
        while (true)
        {
            var item = await reader.ReadAsync();

            try
            {
                var sar = item.Group.Roles.First(x => x.RoleId == item.RoleId);

                if (item.User.RoleIds.Contains(item.RoleId))
                {
                    item.CompletionTask.TrySetResult(new SarAlreadyHasRole());
                    continue;
                }

                if (item.Group.RoleReq is { } rid)
                {
                    if (!item.User.RoleIds.Contains(rid))
                    {
                        item.CompletionTask.TrySetResult(new SarRoleRequirement(rid));
                        continue;
                    }

                    // passed
                }

                // check level requirement
                if (sar.LevelReq > 0)
                {
                    await using var ctx = _db.GetDbContext();
                    var xpStats = await ctx.GetTable<UserXpStats>().GetGuildUserXp(sar.GuildId, item.User.Id);
                    var lvlData = new LevelStats(xpStats?.Xp ?? 0);

                    if (lvlData.Level < sar.LevelReq)
                    {
                        item.CompletionTask.TrySetResult(new SarLevelRequirement(sar.LevelReq));
                        continue;
                    }

                    // passed
                }

                if (item.Group.IsExclusive)
                {
                    var rolesToRemove = item.Group.Roles
                                            .Where(x => item.User.RoleIds.Contains(x.RoleId))
                                            .Select(x => x.RoleId)
                                            .ToArray();
                    if (rolesToRemove.Length > 0)
                        await item.User.RemoveRolesAsync(rolesToRemove);
                }

                await item.User.AddRoleAsync(item.RoleId);

                item.CompletionTask.TrySetResult(new Success());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unknown error ocurred in SAR runner: {Error}", ex.Message);
                item.CompletionTask.TrySetResult(new Error());
            }
        }
    }

    public async Task Add(SarAssignerDataItem item)
    {
        await _channel.Writer.WriteAsync(item);
    }
}

public sealed class SarAssignerDataItem
{
    public required SarGroup Group { get; init; }
    public required IGuildUser User { get; init; }
    public required ulong RoleId { get; init; }
    public required TaskCompletionSource<SarAssignResult> CompletionTask { get; init; }
}

[GenerateOneOf]
public sealed partial class SarAssignResult
    : OneOfBase<Success, Error, SarLevelRequirement, SarRoleRequirement, SarAlreadyHasRole, SarInsuffPerms>
{
}

public record class SarLevelRequirement(int Level);

public record class SarRoleRequirement(ulong RoleId);

public record class SarAlreadyHasRole();

public record class SarInsuffPerms();