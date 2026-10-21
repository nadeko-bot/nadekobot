using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Xp;
using NadekoBot.Modules.Xp.Services;
using OneOf;
using OneOf.Types;
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
                              .Where(Queries.GuildOnShard<SarAutoDelete>(x => x.GuildId, _creds.TotalShards, _client.ShardId))
                              .Where(x => x.IsEnabled)
                              .Select(x => x.GuildId)
                              .ToListAsyncLinqToDB();

        _sarAds = new(guilds);
    }
}

public sealed class SarAssignerService(DbService db, XpFormulaService xpFormula) : INService, IReadyExecutor
{
    private readonly Channel<SarAssignerDataItem> _channel =
        Channel.CreateBounded<SarAssignerDataItem>(100);

    public Task OnReadyAsync()
    {
        _ = Task.Run(AssignLoopInternalAsync);
        return Task.CompletedTask;
    }

    private async Task AssignLoopInternalAsync()
    {
        var reader = _channel.Reader;
        while (true)
        {
            var item = await reader.ReadAsync();

            try
            {
                item.CompletionTask.TrySetResult(await AssignAsync(item.Group, item.User, item.RoleId));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unknown error ocurred in SAR runner: {Error}", ex.Message);
                item.CompletionTask.TrySetResult(new Error());
            }
        }
    }

    public async Task<SarAssignResult> AssignAsync(SarGroup group, IGuildUser user, ulong roleId)
    {
        var sar = group.Roles.First(x => x.RoleId == roleId);

        if (user.RoleIds.Contains(roleId))
            return new SarAlreadyHasRole();

        // a required role which was deleted can not be met by anyone, so it no longer counts
        if (group.RoleReq is { } rid
            && user.Guild.GetRole(rid) is not null
            && !user.RoleIds.Contains(rid))
            return new SarRoleRequirement(rid);

        if (sar.LevelReq > 0)
        {
            await using var ctx = db.GetDbContext();
            var xpStats = await ctx.GetTable<UserXpStats>().GetGuildUserXp(sar.GuildId, user.Id);
            var f = xpFormula.GetFormula(sar.GuildId);
            var lvlData = new LevelStats(xpStats?.Xp ?? 0, f.A, f.C);

            if (lvlData.Level < sar.LevelReq)
                return new SarLevelRequirement(sar.LevelReq);
        }

        var role = user.Guild.GetRole(roleId);
        if (role is null)
            return new Error();

        // checked before any call, a 403 counts toward Discord's invalid request limit
        var botHierarchy = await GetBotHierarchyAsync(user.Guild);
        if (role.IsManaged || role.Position >= botHierarchy)
            return new SarInsuffPerms();

        if (group.IsExclusive)
        {
            var rolesToRemove = new List<ulong>();
            foreach (var x in group.Roles)
            {
                if (x.RoleId != roleId
                    && user.RoleIds.Contains(x.RoleId)
                    && user.Guild.GetRole(x.RoleId) is { } r
                    && r.Position < botHierarchy)
                    rolesToRemove.Add(x.RoleId);
            }

            if (rolesToRemove.Count > 0)
                await user.RemoveRolesAsync(rolesToRemove);
        }

        await user.AddRoleAsync(roleId);

        return new Success();
    }

    private static async Task<int> GetBotHierarchyAsync(IGuild guild)
    {
        var bot = await guild.GetCurrentUserAsync();
        if (bot.Id == guild.OwnerId)
            return int.MaxValue;

        var top = 0;
        foreach (var rid in bot.RoleIds)
        {
            if (guild.GetRole(rid) is { } r && r.Position > top)
                top = r.Position;
        }

        return top;
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