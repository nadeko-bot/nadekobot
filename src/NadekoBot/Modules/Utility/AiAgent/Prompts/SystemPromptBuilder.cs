using System.Text;

namespace NadekoBot.Modules.Utility.AiAgent.Prompts;

// Per-turn values in SOUL or OPERATOR. The rest of the system prompt is the same for every server,
// so providers can cache it once for all requests.
public readonly record struct TurnTokens(string GuildName, string ChannelName, string UserName);

public sealed class SystemPromptBuilder(
    PromptLibrary promptLibrary,
    IAiToolRegistry toolRegistry,
    DiscordSocketClient client) : INService
{
    private const string GUILD_NAME_TOKEN = "{guildName}";
    private const string CHANNEL_NAME_TOKEN = "{channelName}";
    private const string USER_NAME_TOKEN = "{userName}";
    private const int MAX_LISTED_CHANNELS = 50;

    // Text is null when the prompts use per-turn tokens, and then each turn renders its own prompt.
    private sealed record CachedPrompt(PromptSnapshot Snapshot, string BotName, ulong BotId, string? Text);

    private CachedPrompt? _cached;
    private List<string>? _guidance;

    public string GetSystemPrompt(AiToolContext context)
    {
        var self = client.CurrentUser;
        var botName = PromptSanitizer.Sanitize(self.GlobalName ?? self.Username);
        var botId = self.Id;
        var snapshot = promptLibrary.Snapshot;

        var cached = Volatile.Read(ref _cached);
        if (cached is null
            || !ReferenceEquals(cached.Snapshot, snapshot)
            || cached.BotId != botId
            || !string.Equals(cached.BotName, botName, StringComparison.Ordinal))
        {
            var text = HasTurnTokens(snapshot)
                ? null
                : Compose(snapshot, botName, botId, GetGuidance(), null);

            cached = new(snapshot, botName, botId, text);
            Volatile.Write(ref _cached, cached);
        }

        if (cached.Text is not null)
            return cached.Text;

        var turn = new TurnTokens(
            PromptSanitizer.Sanitize(context.Guild.Name),
            PromptSanitizer.Sanitize(context.SourceChannel.Name),
            PromptSanitizer.Sanitize(context.User.DisplayName));

        return Compose(snapshot, botName, botId, GetGuidance(), turn);
    }

    // Stays the same for the whole session, so it goes before the channel history.
    public async Task<string> BuildContextAsync(AiToolContext context)
    {
        var botUser = await context.Guild.GetCurrentUserAsync();
        var channels = await context.Guild.GetTextChannelsAsync();

        var visible = new List<ITextChannel>(channels.Count);
        foreach (var c in channels)
        {
            if (context.User.GetPermissions(c).ViewChannel)
                visible.Add(c);
        }

        visible.Sort(static (a, b) => a.Position.CompareTo(b.Position));

        var sb = new StringBuilder(1024);
        sb.AppendLine("CONTEXT:");
        sb.Append("- Server: ").AppendLine(PromptSanitizer.Sanitize(context.Guild.Name));
        sb.Append("- Bot identity: ").Append(PromptSanitizer.Sanitize(botUser.DisplayName))
          .Append(" (ID: ").Append(botUser.Id).AppendLine(")");
        sb.AppendLine("- Available channels:");

        var count = Math.Min(visible.Count, MAX_LISTED_CHANNELS);
        for (var i = 0; i < count; i++)
        {
            var ch = visible[i];
            sb.Append('#').Append(PromptSanitizer.Sanitize(ch.Name)).Append(" (ID: ").Append(ch.Id).AppendLine(")");
        }

        return sb.ToString();
    }

    // Changes on every turn, so it goes into the last message of the request.
    public static string BuildTurn(AiToolContext context, DateTimeOffset now, string prompt)
    {
        var sb = new StringBuilder(256 + prompt.Length);
        sb.AppendLine("CURRENT TURN:");
        sb.Append("- Current channel: #").Append(PromptSanitizer.Sanitize(context.SourceChannel.Name))
          .Append(" (ID: ").Append(context.SourceChannel.Id).AppendLine(")");
        sb.Append("- User: ").Append(PromptSanitizer.Sanitize(context.User.DisplayName))
          .Append(" (ID: ").Append(context.User.Id).AppendLine(")");
        sb.Append("- Current time: ").Append(now.ToUnixTimeSeconds())
          .Append(" (").Append(now.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine(" UTC)");
        sb.AppendLine();
        sb.Append(prompt);
        return sb.ToString();
    }

    public static string Compose(
        PromptSnapshot snapshot,
        string botName,
        ulong botId,
        IReadOnlyList<string> guidance,
        TurnTokens? turn)
    {
        var soul = ReplaceTokens(snapshot.Soul, botName, botId, turn);
        var operatorDoc = ReplaceTokens(snapshot.Operator, botName, botId, turn);

        var sb = new StringBuilder(8192);

        if (soul.Length > 0)
            sb.AppendLine(soul);

        if (operatorDoc.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(operatorDoc);
        }

        sb.AppendLine();
        sb.AppendLine(DefaultPrompts.PlatformGuidance);

        if (guidance.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("TOOL USAGE:");
            for (var i = 0; i < guidance.Count; i++)
            {
                sb.AppendLine(guidance[i]);
                if (i < guidance.Count - 1)
                    sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public static bool HasTurnTokens(PromptSnapshot snapshot)
        => HasTurnTokens(snapshot.Soul) || HasTurnTokens(snapshot.Operator);

    private static bool HasTurnTokens(string text)
        => text.Contains(GUILD_NAME_TOKEN, StringComparison.Ordinal)
           || text.Contains(CHANNEL_NAME_TOKEN, StringComparison.Ordinal)
           || text.Contains(USER_NAME_TOKEN, StringComparison.Ordinal);

    // The tool set is fixed for the lifetime of the process.
    private List<string> GetGuidance()
        => _guidance ??= CollectToolGuidance(toolRegistry.GetAllTools());

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

    private static string ReplaceTokens(string input, string botName, ulong botId, TurnTokens? turn)
    {
        if (input.Length == 0)
            return input;

        var result = input
            .Replace("{botName}", botName, StringComparison.Ordinal)
            .Replace("{botId}", botId.ToString(), StringComparison.Ordinal);

        if (turn is not { } t)
            return result;

        return result
            .Replace(GUILD_NAME_TOKEN, t.GuildName, StringComparison.Ordinal)
            .Replace(CHANNEL_NAME_TOKEN, t.ChannelName, StringComparison.Ordinal)
            .Replace(USER_NAME_TOKEN, t.UserName, StringComparison.Ordinal);
    }
}
