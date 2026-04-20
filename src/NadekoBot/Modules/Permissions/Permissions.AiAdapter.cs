using NadekoBot.AiAgent;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Permissions.Services;
using NadekoBot.Modules.Utility.AiAgent;

namespace NadekoBot.Modules.Permissions;

public sealed class PermissionsAiAdapter(PermissionService perms) : IAiToolGroup, INService
{
    public string GroupName => "permissions";
    public string GroupDescription => "Server command permissions: the configured permission chain and verbose mode.";

    [AiTool("get_permission_chain", "Returns the ordered list of permission rules configured for this server. Earlier rules override later ones.")]
    public Task<PermissionChainDto> GetPermissionChain(AiToolContext ctx)
    {
        var cache = perms.GetCacheFor(ctx.Guild.Id);
        if (cache is null)
            return Task.FromResult(new PermissionChainDto(ctx.Guild.Id, true, null, []));

        var entries = new List<PermissionEntryDto>();
        if (cache.Permissions is { } list)
        {
            foreach (var p in list)
            {
                entries.Add(new(
                    p.Index,
                    p.PrimaryTarget.ToString(),
                    p.PrimaryTargetId,
                    p.SecondaryTarget.ToString(),
                    p.SecondaryTargetName,
                    p.State,
                    p.IsCustomCommand));
            }
        }

        return Task.FromResult(new PermissionChainDto(
            ctx.Guild.Id,
            cache.Verbose,
            cache.PermRole,
            entries));
    }
}

public sealed record PermissionChainDto(
    ulong GuildId,
    bool Verbose,
    string? PermRole,
    List<PermissionEntryDto> Rules);

public readonly record struct PermissionEntryDto(
    int Index,
    string PrimaryTarget,
    ulong PrimaryTargetId,
    string SecondaryTarget,
    string? SecondaryTargetName,
    bool State,
    bool IsCustomCommand);
