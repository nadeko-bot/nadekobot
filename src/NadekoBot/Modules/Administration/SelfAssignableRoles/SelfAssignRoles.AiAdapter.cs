using NadekoBot.AiAgent;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Modules.Utility.AiAgent;

namespace NadekoBot.Modules.Administration.SelfAssignableRoles;

public sealed class SelfAssignRolesAiAdapter(SelfAssignedRolesService sar) : IAiToolGroup, INService
{
    public string GroupName => "self_assign_roles";
    public string GroupDescription => "Self-assignable roles: available roles grouped by exclusivity and level requirements.";

    [AiTool("list_self_assignable_roles", "Returns the self-assignable role groups configured in this server, with each group's roles and requirements.")]
    public async Task<SelfAssignableRolesDto> ListSelfAssignableRoles(AiToolContext ctx)
    {
        var groups = await sar.GetSarsAsync(ctx.Guild.Id);

        var result = new List<SarGroupDto>(groups.Count);
        foreach (var g in groups)
        {
            var roles = new List<SarRoleDto>(g.Roles.Count);
            foreach (var r in g.Roles)
                roles.Add(new(r.RoleId, r.LevelReq));

            result.Add(new(
                g.GroupNumber,
                g.Name,
                g.IsExclusive,
                g.RoleReq,
                roles));
        }

        return new(ctx.Guild.Id, result);
    }
}

public sealed record SelfAssignableRolesDto(ulong GuildId, List<SarGroupDto> Groups);

public sealed record SarGroupDto(
    int GroupNumber,
    string? Name,
    bool IsExclusive,
    ulong? RequiredRoleId,
    List<SarRoleDto> Roles);

public readonly record struct SarRoleDto(ulong RoleId, int LevelRequired);
