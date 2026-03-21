using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Administration;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Orchestrates AI agent invocations. Owner-only during alpha.
/// </summary>
public sealed class AiAgentService(
    IAiAgentSession agentSession,
    IAiToolRegistry toolRegistry,
    AiAgentConfigService configService,
    CommandSearchService searchService,
    ConversationWindowTracker conversationTracker,
    IBotCredsProvider credsProvider,
    DiscordSocketClient client,
    IMessageSenderService sender) : INService, IExecOnMessage, IExecNoCommand, IReadyExecutor
{
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _activeSessions = new();
    private readonly ConcurrentDictionary<ulong, System.Collections.Concurrent.ConcurrentQueue<QueuedMessage>> _pendingMessages = new();
    private readonly ConcurrentDictionary<ulong, ChannelMessageBuffer> _channelBuffers = new();

    private sealed record QueuedMessage(IGuild Guild, ITextChannel Channel, IUserMessage Message, string Text);

    private const int MAX_SNAPSHOT_CONTENT_LENGTH = 500;
    private const string BOT_TOKEN = "<bot>";
    private static readonly string[] _namePrefixes = ["hey", "hi", "yo", "ok", "dear"];

    /// <summary>
    /// Priority higher than other handlers so agent takes precedence when enabled
    /// </summary>
    public int Priority
        => 3;

    /// <summary>
    /// Starts the background expiry loop for channel memory buffers and conversation windows
    /// </summary>
    public Task OnReadyAsync()
    {
        _ = Task.Run(RunMemoryExpiryLoopAsync);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Periodically removes expired channel memory buffers and conversation windows
    /// </summary>
    private async Task RunMemoryExpiryLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var expiryMinutes = configService.Data.MemoryIdleExpiryMinutes;
                var cutoff = DateTime.UtcNow.AddMinutes(-expiryMinutes);

                foreach (var (channelId, buffer) in _channelBuffers)
                {
                    if (buffer.LastAccessedUtc < cutoff)
                        _channelBuffers.TryRemove(channelId, out _);
                }

                conversationTracker.CleanExpired(configService.Data.FollowUpWindowSeconds);

                foreach (var userId in _pendingMessages.Keys)
                {
                    if (!_activeSessions.ContainsKey(userId))
                        _pendingMessages.TryRemove(userId, out _);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in agent memory expiry loop");
            }
        }
    }

    /// <summary>
    /// Handles @mention trigger and passive buffer observation.
    /// Runs before command parsing so explicit @mention always takes priority.
    /// </summary>
    public async Task<bool> ExecOnMessageAsync(IGuild guild, IUserMessage msg)
    {
        if (!configService.Data.Enabled)
            return false;

        if (guild is not SocketGuild)
            return false;

        var channel = msg.Channel as ITextChannel;
        if (channel is null)
            return false;

        if (_channelBuffers.TryGetValue(channel.Id, out var buffer))
            buffer.Push(CreateSnapshot(msg));

        if (msg is DoAsUserMessage || msg.Author.IsBot)
            return false;

        if (!credsProvider.GetCreds().IsOwner(msg.Author))
            return false;

        var nadekoId = client.CurrentUser.Id;

        var normalMention = $"<@{nadekoId}>";
        var nickMention = $"<@!{nadekoId}>";

        if (msg.Content.StartsWith(normalMention, StringComparison.InvariantCulture))
        {
            var query = msg.Content[normalMention.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(query))
                return await TryRunAgentAsync(guild, channel, msg, query);
        }

        if (msg.Content.StartsWith(nickMention, StringComparison.InvariantCulture))
        {
            var query = msg.Content[nickMention.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(query))
                return await TryRunAgentAsync(guild, channel, msg, query);
        }

        return false;
    }

    /// <summary>
    /// Handles active conversation window, reply+intent, and name+intent triggers.
    /// Runs only when no command matched, so prefixed commands are never intercepted.
    /// </summary>
    public async Task ExecOnNoCommandAsync(IGuild guild, IUserMessage msg)
    {
        if (!configService.Data.Enabled)
            return;

        if (guild is not SocketGuild)
            return;

        var channel = msg.Channel as ITextChannel;
        if (channel is null)
            return;

        if (msg is DoAsUserMessage || msg.Author.IsBot)
            return;

        if (!credsProvider.GetCreds().IsOwner(msg.Author))
            return;

        var config = configService.Data;
        var nadekoId = client.CurrentUser.Id;

        if (config.FollowUpWindowSeconds > 0
            && conversationTracker.IsActive(msg.Author.Id, channel.Id, config.FollowUpWindowSeconds))
        {
            var query = msg.Content.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                await TryRunAgentAsync(guild, channel, msg, query);
                return;
            }
        }

        if (msg.ReferencedMessage?.Author?.Id == nadekoId
            && searchService.IsReady
            && !string.IsNullOrWhiteSpace(msg.Content))
        {
            var query = msg.Content.Trim();
            var textForClassification = query.Contains(BOT_TOKEN, StringComparison.Ordinal)
                ? query
                : $"{BOT_TOKEN} {query}";

            if (searchService.IsCommandIntent(textForClassification))
            {
                await TryRunAgentAsync(guild, channel, msg, query);
                return;
            }
        }

        if (config.NameTriggerEnabled && searchService.IsReady && guild is SocketGuild sg)
        {
            var namesToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(sg.CurrentUser?.Nickname))
                namesToCheck.Add(sg.CurrentUser.Nickname);
            if (!string.IsNullOrWhiteSpace(sg.CurrentUser?.DisplayName))
                namesToCheck.Add(sg.CurrentUser.DisplayName);
            if (!string.IsNullOrWhiteSpace(client.CurrentUser.Username))
                namesToCheck.Add(client.CurrentUser.Username);

            string? matchedName = null;
            foreach (var name in namesToCheck)
            {
                if (msg.Content.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    matchedName = name;
                    break;
                }
            }

            if (matchedName is not null)
            {
                var normalized = NormalizeBotName(msg.Content, matchedName);
                if (!string.IsNullOrWhiteSpace(normalized) && searchService.IsCommandIntent(normalized))
                {
                    var query = StripBotName(msg.Content, matchedName).Trim();
                    await TryRunAgentAsync(guild, channel, msg, query);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Replaces the bot name with a &lt;bot&gt; token for intent classification.
    /// Preserves sentence grammar so "how much money does BotName have" becomes
    /// "how much money does &lt;bot&gt; have" instead of broken "how much money does have".
    /// </summary>
    private static string NormalizeBotName(string content, string botName)
    {
        var idx = content.IndexOf(botName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return content;

        var before = content[..idx];
        var after = content[(idx + botName.Length)..];

        var trimmedBefore = before.Trim();
        foreach (var p in _namePrefixes)
        {
            if (trimmedBefore.Equals(p, StringComparison.OrdinalIgnoreCase))
            {
                before = "";
                break;
            }
        }

        return $"{before}{BOT_TOKEN}{after}".Trim();
    }

    /// <summary>
    /// Strips the bot name from a message, handling common patterns like "hey {name}" or "{name},"
    /// </summary>
    private static string StripBotName(string content, string botName)
    {
        var idx = content.IndexOf(botName, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return content;

        var before = content[..idx].Trim();
        var after = content[(idx + botName.Length)..].TrimStart(',', '!', '?', ' ', '\t');

        foreach (var p in _namePrefixes)
        {
            if (before.Equals(p, StringComparison.OrdinalIgnoreCase))
            {
                before = "";
                break;
            }
        }

        return $"{before} {after}".Trim();
    }

    /// <summary>
    /// Run the AI agent for a user's prompt. Returns false if the agent is disabled or misconfigured.
    /// </summary>
    public async Task<bool> TryRunAgentAsync(
        IGuild guild,
        ITextChannel channel,
        IUserMessage message,
        string prompt)
    {
        var config = configService.Data;

        if (!config.Enabled)
            return false;

        if (string.IsNullOrWhiteSpace(credsProvider.GetCreds().AiApiKey) && config.Backend != "nadeko")
        {
            await sender.Response(channel)
                        .Error("AI agent is not configured. The bot owner must set aiApiKey in data/creds.yml.")
                        .SendAsync();
            return true;
        }

        var guildUser = await guild.GetUserAsync(message.Author.Id);
        if (guildUser is null)
            return false;

        var userId = message.Author.Id;

        if (_activeSessions.ContainsKey(userId))
        {
            var queue = _pendingMessages.GetOrAdd(userId, _ => new());
            queue.Enqueue(new(guild, channel, message, prompt));
            return true;
        }

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        if (!_activeSessions.TryAdd(userId, cts))
        {
            cts.Dispose();
            return false;
        }

        try
        {
            var context = new AiToolContext
            {
                Guild = guild,
                SourceChannel = channel,
                User = guildUser,
                TriggerMessage = message,
                CancellationToken = cts.Token
            };

            await EnsureChannelBufferAsync(channel, config);

            var allowedSet = config.AllowedTools.Count > 0
                ? config.AllowedTools.ToHashSet()
                : null;

            var tools = allowedSet is null
                ? toolRegistry.GetAllTools()
                : toolRegistry.GetAllTools().Where(t => allowedSet.Contains(t.Name)).ToList();

            var schemas = allowedSet is null
                ? toolRegistry.GetToolSchemas()
                : toolRegistry.GetToolSchemas(allowedSet);

            _ = channel.TriggerTypingAsync();

            var systemPrompt = await BuildSystemPromptAsync(config, context);
            var channelHistory = BuildChannelHistoryXml(channel, message.Id);

            var result = await agentSession.RunAsync(
                prompt,
                context,
                tools,
                schemas,
                config,
                systemPrompt,
                channelHistory,
                cts.Token);

            if (result.TryPickT0(out var success, out var error))
            {
                IUserMessage sentMsg;
                var smart = SmartText.CreateFrom(success.Response);

                if (smart is SmartEmbedText or SmartEmbedTextArray)
                {
                    sentMsg = await sender.Response(channel)
                        .Text(smart)
                        .SendAsync();
                }
                else
                {
                    var eb = sender.CreateEmbed(guild.Id)
                                   .WithOkColor()
                                   .WithDescription(success.Response.TrimTo(4096));

                    if (success.ToolCallCount > 0)
                        eb.WithFooter($"Tools used: {success.ToolCallCount}" +
                                      (success.WasCancelled ? " (cancelled)" : ""));

                    sentMsg = await sender.Response(channel)
                        .Embed(eb)
                        .SendAsync();
                }

                if (_channelBuffers.TryGetValue(channel.Id, out var buf))
                {
                    var botUser = await guild.GetCurrentUserAsync();
                    buf.Push(new MessageSnapshot(
                        sentMsg.Id,
                        botUser.Id,
                        PromptSanitizer.Sanitize(botUser.DisplayName),
                        success.Response.TrimTo(MAX_SNAPSHOT_CONTENT_LENGTH) ?? "",
                        DateTimeOffset.UtcNow));
                }

                if (!context.SessionClosed)
                    conversationTracker.Open(message.Author.Id, channel.Id);
            }
            else
            {
                await sender.Response(channel).Error(error.Value).SendAsync();
            }
        }
        catch (OperationCanceledException)
        {
            await sender.Response(channel)
                        .Pending("Agent session was cancelled.")
                        .SendAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in AI agent session for user {UserId}", userId);
            await sender.Response(channel)
                        .Error("An error occurred while running the agent.")
                        .SendAsync();
        }
        finally
        {
            var wasCancelled = cts.IsCancellationRequested;
            _activeSessions.TryRemove(userId, out _);
            cts.Dispose();

            if (wasCancelled)
                _pendingMessages.TryRemove(userId, out _);
        }

        if (_pendingMessages.TryGetValue(userId, out var pending) && !pending.IsEmpty)
        {
            var parts = new List<string>();
            IUserMessage? lastMsg = null;
            ITextChannel? lastChannel = null;
            IGuild? lastGuild = null;
            while (pending.TryDequeue(out var queued))
            {
                parts.Add(queued.Text);
                lastMsg = queued.Message;
                lastChannel = queued.Channel;
                lastGuild = queued.Guild;
            }

            if (parts.Count > 0 && lastMsg is not null && lastChannel is not null && lastGuild is not null)
            {
                var combined = string.Join("\n", parts);
                _ = TryRunAgentAsync(lastGuild, lastChannel, lastMsg, combined);
            }
        }

        return true;
    }

    /// <summary>
    /// Lazily creates a channel buffer on first agent invocation, backfilling from Discord API
    /// </summary>
    private async Task EnsureChannelBufferAsync(ITextChannel channel, AiAgentConfig config)
    {
        if (config.ChannelMessageMemory <= 0)
            return;

        if (_channelBuffers.ContainsKey(channel.Id))
            return;

        var buffer = new ChannelMessageBuffer(config.ChannelMessageMemory);

        var messages = await channel
            .GetMessagesAsync(limit: config.ChannelMessageMemory)
            .FlattenAsync();

        var snapshots = messages
            .OrderBy(m => m.Timestamp)
            .Select(m => CreateSnapshot(m))
            .ToList();

        foreach (var snapshot in snapshots)
            buffer.Push(snapshot);

        _channelBuffers.TryAdd(channel.Id, buffer);
    }

    /// <summary>
    /// Creates a sanitized message snapshot from a Discord message
    /// </summary>
    private static MessageSnapshot CreateSnapshot(IMessage msg)
        => new(
            msg.Id,
            msg.Author.Id,
            PromptSanitizer.Sanitize(msg.Author.Username),
            PromptSanitizer.Sanitize(GetMessageText(msg)).TrimTo(MAX_SNAPSHOT_CONTENT_LENGTH) ?? "",
            msg.Timestamp);

    private static string GetMessageText(IMessage msg)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(msg.Content))
            sb.Append(msg.Content);

        foreach (var embed in msg.Embeds)
        {
            if (!string.IsNullOrWhiteSpace(embed.Title))
                sb.Append($"\n[{embed.Title}]");
            if (!string.IsNullOrWhiteSpace(embed.Description))
                sb.Append($"\n{embed.Description}");
            foreach (var field in embed.Fields)
                sb.Append($"\n{field.Name}: {field.Value}");
            if (embed.Footer.HasValue && !string.IsNullOrWhiteSpace(embed.Footer.Value.Text))
                sb.Append($"\n{embed.Footer.Value.Text}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds XML-formatted channel history from the buffer, excluding the trigger message.
    /// Returns null if no history exists.
    /// </summary>
    private string? BuildChannelHistoryXml(ITextChannel channel, ulong triggerMessageId)
    {
        if (!_channelBuffers.TryGetValue(channel.Id, out var buffer))
            return null;

        var snapshots = buffer.GetMessages();
        if (snapshots.Length == 0)
            return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<channel_history channel_id=\"{channel.Id}\" channel_name=\"{PromptSanitizer.Sanitize(channel.Name)}\">");

        foreach (var s in snapshots)
        {
            if (s.MessageId == triggerMessageId)
                continue;

            sb.AppendLine($"<msg id=\"{s.MessageId}\" author=\"{s.AuthorName}\" author_id=\"{s.AuthorId}\" time=\"{s.Timestamp.ToUnixTimeSeconds()}\">{s.Content}</msg>");
        }

        sb.Append("</channel_history>");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the system prompt with sanitized guild/channel context
    /// </summary>
    private async Task<string> BuildSystemPromptAsync(AiAgentConfig config, AiToolContext context)
    {
        var botUser = await context.Guild.GetCurrentUserAsync();
        var botName = PromptSanitizer.Sanitize(botUser.DisplayName);

        var guildName = PromptSanitizer.Sanitize(context.Guild.Name);
        var channelName = PromptSanitizer.Sanitize(context.SourceChannel.Name);
        var channelId = context.SourceChannel.Id;
        var userName = PromptSanitizer.Sanitize(context.User.DisplayName);

        var channels = await context.Guild.GetTextChannelsAsync();
        var visible = channels
            .Where(c => context.User.GetPermissions(c).ViewChannel)
            .OrderBy(c => c.Position)
            .Take(50)
            .Select(c => $"#{PromptSanitizer.Sanitize(c.Name)} (ID: {c.Id})")
            .ToList();

        var channelList = string.Join("\n", visible);

        var systemPrompt = config.SystemPrompt.Replace("{botName}", botName);
        var now = DateTimeOffset.UtcNow;

        return $"""
            {systemPrompt}

            CONTEXT:
            - Server: {guildName}
            - Bot identity: {botName} (ID: {botUser.Id})
            - Current channel: #{channelName} (ID: {channelId})
            - User: {userName} (ID: {context.User.Id})
            - Current time: {now.ToUnixTimeSeconds()} ({now:yyyy-MM-dd HH:mm:ss} UTC)
            - Available channels:
            {channelList}
            """;
    }

    /// <summary>
    /// Cancel the active agent session for a user. Returns true if a session was found and cancelled.
    /// </summary>
    public bool CancelSession(ulong userId)
    {
        if (_activeSessions.TryRemove(userId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _pendingMessages.TryRemove(userId, out _);
            conversationTracker.CloseAll(userId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Check if a user has an active agent session
    /// </summary>
    public bool HasActiveSession(ulong userId)
        => _activeSessions.ContainsKey(userId);
}
