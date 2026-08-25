using System.Text;

namespace NadekoBot.Modules.Utility.AiAgent.Prompts;

public sealed class SystemPromptBuilder(
    PromptLibrary promptLibrary,
    IAiToolRegistry toolRegistry) : INService
{
    public async Task<string> BuildAsync(AiToolContext context)
    {
        var botUser = await context.Guild.GetCurrentUserAsync();
        var botName = PromptSanitizer.Sanitize(botUser.DisplayName);
        var botId = botUser.Id;
        var guildName = PromptSanitizer.Sanitize(context.Guild.Name);
        var channelName = PromptSanitizer.Sanitize(context.SourceChannel.Name);
        var channelId = context.SourceChannel.Id;
        var userName = PromptSanitizer.Sanitize(context.User.DisplayName);
        var userId = context.User.Id;

        var soul = ReplaceTokens(promptLibrary.GetSoul(), botName, botId, guildName, channelName, userName);
        var operatorDoc = ReplaceTokens(promptLibrary.GetOperatorDoc(), botName, botId, guildName, channelName, userName);

        var channels = await context.Guild.GetTextChannelsAsync();
        var visible = channels
            .Where(c => context.User.GetPermissions(c).ViewChannel)
            .OrderBy(static c => c.Position)
            .Take(50)
            .Select(static c => $"#{PromptSanitizer.Sanitize(c.Name)} (ID: {c.Id})");

        var now = DateTimeOffset.UtcNow;

        var sb = new StringBuilder(4096);

        // Slot 1: Identity (SOUL, operator-editable)
        if (soul.Length > 0)
            sb.AppendLine(soul);

        // Slot 2: Operator rules (OPERATOR, operator-editable)
        if (operatorDoc.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(operatorDoc);
        }

        // Slot 3: Platform guidance (code, always emitted)
        sb.AppendLine();
        sb.AppendLine(DefaultPrompts.PlatformGuidance);

        // Slot 4: Tool usage guidance (code, from IAiTool.SystemGuidance, deduplicated)
        AppendToolGuidance(sb);

        // Slot 5: Dynamic context
        sb.AppendLine();
        sb.AppendLine("CONTEXT:");
        sb.Append("- Server: ").AppendLine(guildName);
        sb.Append("- Bot identity: ").Append(botName).Append(" (ID: ").Append(botId).AppendLine(")");
        sb.Append("- Current channel: #").Append(channelName).Append(" (ID: ").Append(channelId).AppendLine(")");
        sb.Append("- User: ").Append(userName).Append(" (ID: ").Append(userId).AppendLine(")");
        sb.Append("- Current time: ").Append(now.ToUnixTimeSeconds())
          .Append(" (").Append(now.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine(" UTC)");
        sb.AppendLine("- Available channels:");
        foreach (var ch in visible)
            sb.AppendLine(ch);

        return sb.ToString();
    }

    private void AppendToolGuidance(StringBuilder sb)
    {
        var guidances = CollectToolGuidance(toolRegistry.GetAllTools());
        if (guidances.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("TOOL USAGE:");
        for (var i = 0; i < guidances.Count; i++)
        {
            sb.AppendLine(guidances[i]);
            if (i < guidances.Count - 1)
                sb.AppendLine();
        }
    }

    // Deduplicated and sorted, so the prompt is deterministic.
    public static List<string> CollectToolGuidance(IReadOnlyList<IAiTool> tools)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var guidances = new List<string>(tools.Count);

        for (var i = 0; i < tools.Count; i++)
        {
            var g = tools[i].SystemGuidance;
            if (string.IsNullOrWhiteSpace(g))
                continue;
            if (seen.Add(g))
                guidances.Add(g);
        }

        guidances.Sort(StringComparer.Ordinal);
        return guidances;
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static string ReplaceTokens(
        string input,
        string botName,
        ulong botId,
        string guildName,
        string channelName,
        string userName)
    {
        if (input.Length == 0)
            return input;

        return input
            .Replace("{botName}", botName, StringComparison.Ordinal)
            .Replace("{botId}", botId.ToString(), StringComparison.Ordinal)
            .Replace("{guildName}", guildName, StringComparison.Ordinal)
            .Replace("{channelName}", channelName, StringComparison.Ordinal)
            .Replace("{userName}", userName, StringComparison.Ordinal);
    }
}
