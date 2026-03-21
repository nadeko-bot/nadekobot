using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Returns profile information about a guild member.
/// Accepts user ID, mention, or username.
/// </summary>
public sealed partial class GetUserInfoTool : IAiTool, INService
{
    public string Name => "get_user_info";

    public string Description =>
        "Get information about a server member including their roles, join date, account age, and status. " +
        "You can specify the user by ID, mention (like <@123456>), or username.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "user": {
                    "type": "string",
                    "description": "The user to look up - can be an ID, mention like <@123456>, or username"
                }
            },
            "required": ["user"]
        }
        """).RootElement.Clone();

    [GeneratedRegex(@"<@!?(\d+)>")]
    private static partial Regex UserMentionRegex();

    public async Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!arguments.TryGetProperty("user", out var userEl)
            || string.IsNullOrWhiteSpace(userEl.GetString()))
            return "Error: user is required.";

        var input = userEl.GetString()!.Trim();

        var member = await ResolveUserAsync(context, input);
        if (member is null)
            return "Error: User not found in this server.";

        var sb = new StringBuilder();
        sb.AppendLine($"Username: {member.Username}");

        if (!string.IsNullOrWhiteSpace(member.DisplayName)
            && !string.Equals(member.DisplayName, member.Username, StringComparison.Ordinal))
            sb.AppendLine($"Display Name: {member.DisplayName}");

        if (!string.IsNullOrWhiteSpace(member.Nickname))
            sb.AppendLine($"Nickname: {member.Nickname}");

        sb.AppendLine($"ID: {member.Id}");
        sb.AppendLine($"Account Created: {member.CreatedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"Joined Server: {member.JoinedAt?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "Unknown"}");
        sb.AppendLine($"Status: {member.Status}");

        if (member.Activities.Count > 0)
        {
            var activity = member.Activities.First();
            sb.AppendLine($"Activity: {activity.Type} {activity.Name}");
        }

        var roles = member.GetRoles()
            .Where(r => r.Id != context.Guild.EveryoneRole.Id)
            .OrderByDescending(r => r.Position)
            .Select(r => r.Name)
            .ToList();

        if (roles.Count > 0)
            sb.AppendLine($"Roles ({roles.Count}): {string.Join(", ", roles)}");
        else
            sb.AppendLine("Roles: None");

        var avatarUrl = member.GetGuildAvatarUrl() ?? member.GetDisplayAvatarUrl();
        if (!string.IsNullOrWhiteSpace(avatarUrl))
            sb.AppendLine($"Avatar: {avatarUrl}");

        return sb.ToString();
    }

    private static async Task<IGuildUser?> ResolveUserAsync(AiToolContext context, string input)
    {
        var mentionMatch = UserMentionRegex().Match(input);
        if (mentionMatch.Success && ulong.TryParse(mentionMatch.Groups[1].Value, out var mentionId))
            return await context.Guild.GetUserAsync(mentionId);

        if (ulong.TryParse(input, out var rawId))
            return await context.Guild.GetUserAsync(rawId);

        var users = await context.Guild.GetUsersAsync();
        return users.FirstOrDefault(u =>
            string.Equals(u.Username, input, StringComparison.OrdinalIgnoreCase)
            || string.Equals(u.DisplayName, input, StringComparison.OrdinalIgnoreCase)
            || string.Equals(u.Nickname, input, StringComparison.OrdinalIgnoreCase));
    }
}
