using NadekoBot.AiAgent;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

public sealed partial class MembersAiAdapter : IAiToolGroup, INService
{
    public string GroupName => "members";
    public string GroupDescription => "Server members: profile, roles, join date, status.";

    [GeneratedRegex(@"<@!?(\d+)>")]
    private static partial Regex UserMentionRegex();

    [AiTool("get_user_info", "Returns profile info for a server member: roles, join date, account age, status, avatar. Accepts user ID, mention, or username.")]
    public async Task<UserInfoDto> GetUserInfo(
        AiToolContext ctx,
        [AiParam("User ID, mention like <@123456>, or username")] string user)
    {
        var member = await ResolveUserInternalAsync(ctx, user);
        if (member is null)
            return new(0, null!, null, null, default, null, default, null, null, null, "User not found in this server.");

        var roles = member.GetRoles()
            .Where(r => r.Id != ctx.Guild.EveryoneRole.Id)
            .OrderByDescending(static r => r.Position)
            .Select(static r => new UserRoleDto(r.Id, r.Name))
            .ToList();

        var activity = member.Activities.Count > 0
            ? new UserActivityDto(member.Activities.First().Type.ToString(), member.Activities.First().Name)
            : (UserActivityDto?)null;

        var avatarUrl = member.GetGuildAvatarUrl() ?? member.GetDisplayAvatarUrl();

        return new(
            member.Id,
            member.Username,
            member.DisplayName,
            member.Nickname,
            member.CreatedAt,
            member.JoinedAt,
            member.Status.ToString(),
            activity,
            roles,
            avatarUrl,
            null);
    }

    [AiTool("list_user_roles", "Returns the list of roles assigned to a server member, ordered by position.")]
    public async Task<UserRolesDto> ListUserRoles(
        AiToolContext ctx,
        [AiParam("User ID, mention like <@123456>, or username")] string user)
    {
        var member = await ResolveUserInternalAsync(ctx, user);
        if (member is null)
            return new(0, [], "User not found in this server.");

        var roles = member.GetRoles()
            .Where(r => r.Id != ctx.Guild.EveryoneRole.Id)
            .OrderByDescending(static r => r.Position)
            .Select(static r => new UserRoleDto(r.Id, r.Name))
            .ToList();

        return new(member.Id, roles, null);
    }

    private static async Task<IGuildUser?> ResolveUserInternalAsync(AiToolContext ctx, string input)
    {
        input = input.Trim();

        var mentionMatch = UserMentionRegex().Match(input);
        if (mentionMatch.Success && ulong.TryParse(mentionMatch.Groups[1].Value, out var mentionId))
            return await ctx.Guild.GetUserAsync(mentionId);

        if (ulong.TryParse(input, out var rawId))
            return await ctx.Guild.GetUserAsync(rawId);

        var users = await ctx.Guild.GetUsersAsync();
        foreach (var u in users)
        {
            if (string.Equals(u.Username, input, StringComparison.InvariantCultureIgnoreCase)
                || string.Equals(u.DisplayName, input, StringComparison.InvariantCultureIgnoreCase)
                || string.Equals(u.Nickname, input, StringComparison.InvariantCultureIgnoreCase))
                return u;
        }

        return null;
    }
}

public sealed record UserInfoDto(
    ulong Id,
    string Username,
    string? DisplayName,
    string? Nickname,
    DateTimeOffset CreatedAt,
    DateTimeOffset? JoinedAt,
    string? Status,
    UserActivityDto? Activity,
    List<UserRoleDto>? Roles,
    string? AvatarUrl,
    string? Error);

public readonly record struct UserActivityDto(string Type, string Name);

public readonly record struct UserRoleDto(ulong Id, string Name);

public sealed record UserRolesDto(ulong UserId, List<UserRoleDto> Roles, string? Error);
