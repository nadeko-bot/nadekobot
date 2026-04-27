using System.Collections.Immutable;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration;
using NadekoBot.Modules.Patronage;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Orchestrates AI agent invocations. Available to owner and active patrons.
/// </summary>
public sealed class AiAgentService(
    IAiAgentSession agentSession,
    IAiToolRegistry toolRegistry,
    AiAgentConfigService configService,
    CommandSearchService searchService,
    ConversationWindowTracker conversationTracker,
    IBotCredsProvider credsProvider,
    DiscordSocketClient client,
    IMessageSenderService sender,
    IPatronageService patronageService,
    PatronageConfig patronageConfig,
    Prompts.SystemPromptBuilder systemPromptBuilder,
    DbService db) : INService, IExecOnMessage, IExecNoCommand, IReadyExecutor
{
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _activeSessions = new();
    private readonly ConcurrentDictionary<ulong, System.Collections.Concurrent.ConcurrentQueue<QueuedMessage>> _pendingMessages = new();
    private readonly ConcurrentDictionary<ulong, ChannelMessageBuffer> _channelBuffers = new();
    private readonly ConcurrentDictionary<ulong, (bool Allowed, DateTime ExpiresUtc)> _allowedCache = new();
    private readonly ConcurrentDictionary<ulong, ImmutableArray<AiAgentGuildSkill>> _skillCache = new();

    private sealed record QueuedMessage(IGuild Guild, ITextChannel Channel, IUserMessage Message, string Text);

    public const int MAX_SKILLS_PER_GUILD = 10;
    public const int MAX_SKILL_INSTRUCTION_LENGTH = 2000;
    public const int MAX_SKILL_NAME_LENGTH = 50;
    private const string BOT_TOKEN = "<bot>";
    private static readonly string[] _namePrefixes = ["hey", "hi", "yo", "ok", "dear"];
    private bool _credsWarningLogged;

    /// <summary>
    /// Priority higher than other handlers so agent takes precedence when enabled
    /// </summary>
    public int Priority
        => 3;

    /// <summary>
    /// Starts the background expiry loop for channel memory buffers and conversation windows.
    /// Also subscribes to MessageReceived so every channel message (including the bot's own
    /// command output and replies) feeds the per-channel buffer once a session has opened it.
    /// </summary>
    public async Task OnReadyAsync()
    {
        await LoadSkillCacheAsync();
        client.MessageReceived += OnMessageReceivedFeederAsync;
        client.MessageDeleted += OnMessageDeletedFeederAsync;
        _ = Task.Run(RunMemoryExpiryLoopAsync);
    }

    /// <summary>
    /// Single canonical feeder for every channel buffer. Runs for every Discord message
    /// regardless of author so the agent sees its own command output between turns.
    /// No-ops when no buffer exists for the channel (no active session in that channel).
    /// </summary>
    private Task OnMessageReceivedFeederAsync(SocketMessage msg)
    {
        if (msg is not SocketUserMessage userMsg)
            return Task.CompletedTask;

        if (msg.Channel is not SocketTextChannel)
            return Task.CompletedTask;

        if (!_channelBuffers.TryGetValue(msg.Channel.Id, out var buffer))
            return Task.CompletedTask;

        buffer.Push(CreateSnapshot(userMsg));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pops a deleted message from the channel buffer so channel_history reflects what
    /// is actually still in the channel. No-op when no buffer or no match.
    /// </summary>
    private Task OnMessageDeletedFeederAsync(
        Cacheable<IMessage, ulong> cachedMsg,
        Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        if (_channelBuffers.TryGetValue(cachedChannel.Id, out var buffer))
            buffer.TryRemove(cachedMsg.Id);
        return Task.CompletedTask;
    }

    private async Task LoadSkillCacheAsync()
    {
        var creds = credsProvider.GetCreds();
        await using var ctx = db.GetDbContext();
        var skills = await ctx.GetTable<AiAgentGuildSkill>()
            .Where(Queries.GuildOnShard<AiAgentGuildSkill>(x => x.GuildId, creds.TotalShards, client.ShardId))
            .ToListAsyncLinqToDB();

        foreach (var group in skills.GroupBy(x => x.GuildId))
            _skillCache[group.Key] = group.ToImmutableArray();
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

                var now = DateTime.UtcNow;
                foreach (var (userId, entry) in _allowedCache)
                {
                    if (entry.ExpiresUtc <= now)
                        _allowedCache.TryRemove(userId, out _);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in agent memory expiry loop");
            }
        }
    }

    /// <summary>
    /// Handles @mention trigger detection.
    /// Runs before command parsing so explicit @mention always takes priority.
    /// Buffer ingestion is handled by <see cref="OnMessageReceivedFeederAsync"/>.
    /// </summary>
    public async ValueTask<bool> ExecOnMessageAsync(IGuild? guild, IUserMessage msg)
    {
        if (!configService.Data.Enabled)
            return false;

        if (!HasValidAiCreds())
            return false;

        if (guild is not SocketGuild)
            return false;

        var channel = msg.Channel as ITextChannel;
        if (channel is null)
            return false;

        if (msg is DoAsUserMessage || msg.Author.IsBot)
            return false;

        var nadekoId = client.CurrentUser.Id;

        var normalMention = $"<@{nadekoId}>";
        var nickMention = $"<@!{nadekoId}>";

        string? query = null;

        if (msg.Content.StartsWith(normalMention, StringComparison.InvariantCulture))
        {
            var q = msg.Content[normalMention.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(q))
                query = q;
        }

        if (query is null && msg.Content.StartsWith(nickMention, StringComparison.InvariantCulture))
        {
            var q = msg.Content[nickMention.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(q))
                query = q;
        }

        if (query is null)
            return false;

        if (!await IsAllowedAsync(msg.Author))
            return false;

        return await TryRunAgentAsync(guild, channel, msg, query);
    }

    /// <summary>
    /// Handles active conversation window, reply+intent, and name+intent triggers.
    /// Runs only when no command matched, so prefixed commands are never intercepted.
    /// </summary>
    public async ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (!configService.Data.Enabled)
            return;

        if (!HasValidAiCreds())
            return;

        if (guild is not SocketGuild)
            return;

        var channel = msg.Channel as ITextChannel;
        if (channel is null)
            return;

        if (msg is DoAsUserMessage || msg.Author.IsBot)
            return;

        var config = configService.Data;
        var nadekoId = client.CurrentUser.Id;

        if (config.FollowUpWindowSeconds > 0
            && conversationTracker.IsActive(msg.Author.Id, channel.Id, config.FollowUpWindowSeconds))
        {
            var query = msg.Content.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                if (!await IsAllowedAsync(msg.Author))
                    return;

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
                if (!await IsAllowedAsync(msg.Author))
                    return;

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
                    if (!await IsAllowedAsync(msg.Author))
                        return;

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

            var systemPrompt = await systemPromptBuilder.BuildAsync(context);
            var enrichedPrompt = BuildSkillPreamble(guild.Id, prompt);
            var triggerMessageId = message.Id;

            var result = await agentSession.RunAsync(
                enrichedPrompt,
                context,
                tools,
                schemas,
                config,
                systemPrompt,
                () => BuildChannelHistoryXml(channel, triggerMessageId),
                cts.Token);

            if (result.TryPickT0(out var success, out var error))
            {
                if (success.AskPending)
                {
                    conversationTracker.Open(message.Author.Id, channel.Id);
                }
                else
                {
                    var smart = SmartText.CreateFrom(success.Response);

                    if (smart is SmartEmbedText or SmartEmbedTextArray)
                    {
                        await sender.Response(channel)
                            .Text(smart)
                            .Split()
                            .SendAsync();
                    }
                    else if (config.UseEmbed)
                    {
                        var eb = sender.CreateEmbed(guild.Id)
                                       .WithOkColor()
                                       .WithDescription(success.Response);

                        if (success.ToolCallCount > 0)
                            eb.WithFooter($"Tools used: {success.ToolCallCount}" +
                                          (success.WasCancelled ? " (cancelled)" : ""));

                        await sender.Response(channel)
                            .Embed(eb)
                            .Split()
                            .SendAsync();
                    }
                    else
                    {
                        await sender.Response(channel)
                            .Text(new SmartPlainText(success.Response))
                            .Split()
                            .SendAsync();
                    }

                    if (!context.SessionClosed)
                        conversationTracker.Open(message.Author.Id, channel.Id);
                }
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

    private const int CHANNEL_BACKFILL_MAX = 100;

    /// <summary>
    /// Lazily creates a channel buffer on first agent invocation, backfilling from Discord API.
    /// Backfill is capped at <see cref="CHANNEL_BACKFILL_MAX"/> (Discord's per-call limit) even
    /// if <see cref="AiAgentConfig.ChannelMessageMemory"/> is larger; the buffer fills the rest
    /// from live MessageReceived events.
    /// </summary>
    private async Task EnsureChannelBufferAsync(ITextChannel channel, AiAgentConfig config)
    {
        if (config.ChannelMessageMemory <= 0)
            return;

        if (_channelBuffers.ContainsKey(channel.Id))
            return;

        var buffer = new ChannelMessageBuffer(config.ChannelMessageMemory);
        var backfillCount = Math.Min(CHANNEL_BACKFILL_MAX, config.ChannelMessageMemory);

        var messages = await channel
            .GetMessagesAsync(limit: backfillCount)
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
            PromptSanitizer.Sanitize(GetMessageText(msg)),
            msg.Timestamp);

    /// <summary>
    /// Renders a Discord message into a labeled-section text representation that the LLM
    /// can read alongside other channel history entries. Captures content, all embed
    /// fields, attachments, stickers, and reply context.
    /// </summary>
    private static string GetMessageText(IMessage msg)
    {
        var sb = new System.Text.StringBuilder();

        if (msg is IUserMessage userMsg && userMsg.ReferencedMessage is { } reply)
        {
            var replyAuthor = reply.Author?.Username ?? "?";
            var replyExcerpt = reply.Content;
            if (!string.IsNullOrEmpty(replyExcerpt))
                sb.Append($"reply_to: {replyAuthor}: {replyExcerpt}\n");
            else
                sb.Append($"reply_to: {replyAuthor}\n");
        }

        if (!string.IsNullOrWhiteSpace(msg.Content))
            sb.Append("text: ").Append(msg.Content).Append('\n');

        foreach (var embed in msg.Embeds)
            AppendEmbedInternal(sb, embed);

        foreach (var att in msg.Attachments)
        {
            sb.Append("attachment: ").Append(att.Filename);
            if (!string.IsNullOrWhiteSpace(att.ContentType))
                sb.Append(" (").Append(att.ContentType).Append(')');
            if (att.Size > 0)
                sb.Append(' ').Append(att.Size).Append('B');
            if (!string.IsNullOrWhiteSpace(att.Url))
                sb.Append(' ').Append(att.Url);
            sb.Append('\n');
        }

        foreach (var sticker in msg.Stickers)
            sb.Append("sticker: ").Append(sticker.Name).Append('\n');

        if (sb.Length > 0 && sb[^1] == '\n')
            sb.Length--;

        return sb.ToString();
    }

    private static void AppendEmbedInternal(System.Text.StringBuilder sb, IEmbed embed)
    {
        sb.Append("embed:\n");

        if (embed.Author is { Name: { } authorName } && !string.IsNullOrWhiteSpace(authorName))
            sb.Append("  author: ").Append(authorName).Append('\n');
        if (!string.IsNullOrWhiteSpace(embed.Title))
            sb.Append("  title: ").Append(embed.Title).Append('\n');
        if (!string.IsNullOrWhiteSpace(embed.Url))
            sb.Append("  link: ").Append(embed.Url).Append('\n');
        if (!string.IsNullOrWhiteSpace(embed.Description))
            sb.Append("  description: ").Append(embed.Description).Append('\n');

        foreach (var field in embed.Fields)
            sb.Append("  field: ").Append(field.Name).Append(" = ").Append(field.Value).Append('\n');

        if (embed.Image is { Url: { } imageUrl } && !string.IsNullOrWhiteSpace(imageUrl))
            sb.Append("  image: ").Append(imageUrl).Append('\n');
        if (embed.Thumbnail is { Url: { } thumbUrl } && !string.IsNullOrWhiteSpace(thumbUrl))
            sb.Append("  thumbnail: ").Append(thumbUrl).Append('\n');

        if (embed.Footer is { Text: { } footerText } && !string.IsNullOrWhiteSpace(footerText))
            sb.Append("  footer: ").Append(footerText).Append('\n');

        if (embed.Timestamp is { } embedTs)
            sb.Append("  timestamp: ").Append(embedTs.ToUnixTimeSeconds()).Append('\n');
    }

    private string? BuildChannelHistoryXml(ITextChannel channel, ulong triggerMessageId)
    {
        if (!_channelBuffers.TryGetValue(channel.Id, out var buffer))
            return null;

        return buffer.BuildHistoryXml(
            channel.Id,
            PromptSanitizer.Sanitize(channel.Name),
            triggerMessageId);
    }



    private bool HasValidAiCreds()
    {
        var creds = credsProvider.GetCreds();
        var ok = !string.IsNullOrWhiteSpace(creds.AiApiKey);

        if (!ok && !_credsWarningLogged)
        {
            _credsWarningLogged = true;
            Log.Warning("AI agent is enabled but AiApiKey is empty in creds.yml. "
                        + "Agent will not run until credentials are set");
        }

        return ok;
    }

    public async Task<bool> AddSkillAsync(ulong guildId, string name, string instruction)
    {
        name = name.ToLowerInvariant();

        if (!_skillCache.TryGetValue(guildId, out var skills))
            skills = [];

        if (skills.Length >= MAX_SKILLS_PER_GUILD)
            return false;

        if (skills.Any(s => s.Name == name))
            return false;

        await using var ctx = db.GetDbContext();
        var id = await ctx.GetTable<AiAgentGuildSkill>()
            .InsertWithInt32IdentityAsync(() => new()
            {
                GuildId = guildId,
                Name = name,
                Instruction = instruction,
                IsEnabled = true
            });

        var newSkill = new AiAgentGuildSkill
        {
            Id = id,
            GuildId = guildId,
            Name = name,
            Instruction = instruction,
            IsEnabled = true
        };

        _skillCache[guildId] = skills.Add(newSkill);
        return true;
    }

    public async Task<bool> RemoveSkillAsync(ulong guildId, string name)
    {
        name = name.ToLowerInvariant();

        await using var ctx = db.GetDbContext();
        var deleted = await ctx.GetTable<AiAgentGuildSkill>()
            .Where(x => x.GuildId == guildId && x.Name == name)
            .DeleteAsync();

        if (deleted == 0)
            return false;

        if (_skillCache.TryGetValue(guildId, out var skills))
        {
            var updated = skills.RemoveAll(s => s.Name == name);
            if (updated.IsEmpty)
                _skillCache.TryRemove(guildId, out _);
            else
                _skillCache[guildId] = updated;
        }

        return true;
    }

    public async Task<bool?> ToggleSkillAsync(ulong guildId, string name)
    {
        name = name.ToLowerInvariant();

        await using var ctx = db.GetDbContext();
        var results = await ctx.GetTable<AiAgentGuildSkill>()
            .Where(x => x.GuildId == guildId && x.Name == name)
            .Set(x => x.IsEnabled, x => !x.IsEnabled)
            .UpdateWithOutputAsync((_, @new) => @new.IsEnabled);

        if (results.Length == 0)
            return null;

        var newState = results[0];

        if (_skillCache.TryGetValue(guildId, out var skills))
        {
            var builder = skills.ToBuilder();
            for (var i = 0; i < builder.Count; i++)
            {
                if (builder[i].Name == name)
                {
                    builder[i] = new()
                    {
                        Id = builder[i].Id,
                        GuildId = guildId,
                        Name = name,
                        Instruction = builder[i].Instruction,
                        IsEnabled = newState
                    };
                    break;
                }
            }

            _skillCache[guildId] = builder.ToImmutable();
        }

        return newState;
    }

    public IReadOnlyList<AiAgentGuildSkill> GetSkills(ulong guildId)
    {
        if (_skillCache.TryGetValue(guildId, out var skills))
            return skills;

        return [];
    }

    public IReadOnlyList<AiAgentGuildSkill> GetSkills(ulong guildId, ulong channelId)
    {
        if (_skillCache.TryGetValue(guildId, out var skills))
            return skills.Where(s => s.ChannelId == channelId).ToImmutableArray();
        return [];
    }

    public async Task<bool> AddSkillAsync(ulong guildId, string name, string instruction, ulong channelId)
    {
        name = name.ToLowerInvariant();
        if (!_skillCache.TryGetValue(guildId, out var skills))
            skills = [];

        if (skills.Length >= MAX_SKILLS_PER_GUILD)
            return false;

        if (skills.Any(s => s.Name == name && s.ChannelId == channelId))
            return false;

        await using var ctx = db.GetDbContext();
        var id = await ctx.GetTable<AiAgentGuildSkill>()
            .InsertWithInt32IdentityAsync(() => new()
            {
                GuildId = guildId,
                ChannelId = channelId,
                Name = name,
                Instruction = instruction,
                IsEnabled = true
            });

        var newSkill = new AiAgentGuildSkill
        {
            Id = id,
            GuildId = guildId,
            ChannelId = channelId,
            Name = name,
            Instruction = instruction,
            IsEnabled = true
        };

        _skillCache[guildId] = skills.Add(newSkill);
        return true;
    }

    public async Task<bool> RemoveSkillAsync(ulong guildId, string name, ulong channelId)
    {
        name = name.ToLowerInvariant();
        await using var ctx = db.GetDbContext();
        var deleted = await ctx.GetTable<AiAgentGuildSkill>()
            .Where(x => x.GuildId == guildId && x.Name == name && x.ChannelId == channelId)
            .DeleteAsync();

        if (deleted == 0)
            return false;

        if (_skillCache.TryGetValue(guildId, out var skills))
        {
            var updated = skills.RemoveAll(s => s.Name == name && s.ChannelId == channelId);
            if (updated.IsEmpty)
                _skillCache.TryRemove(guildId, out _);
            else
                _skillCache[guildId] = updated;
        }

        return true;
    }

    public async Task<bool?> ToggleSkillAsync(ulong guildId, string name, ulong channelId)
    {
        name = name.ToLowerInvariant();
        await using var ctx = db.GetDbContext();
        var results = await ctx.GetTable<AiAgentGuildSkill>()
            .Where(x => x.GuildId == guildId && x.Name == name && x.ChannelId == channelId)
            .Set(x => x.IsEnabled, x => !x.IsEnabled)
            .UpdateWithOutputAsync((_, @new) => @new.IsEnabled);

        if (results.Length == 0)
            return null;

        var newState = results[0];
        if (_skillCache.TryGetValue(guildId, out var skills))
        {
            var builder = skills.ToBuilder();
            for (var i = 0; i < builder.Count; i++)
            {
                if (builder[i].Name == name && builder[i].ChannelId == channelId)
                {
                    builder[i] = new()
                    {
                        Id = builder[i].Id,
                        GuildId = guildId,
                        ChannelId = channelId,
                        Name = name,
                        Instruction = builder[i].Instruction,
                        IsEnabled = newState
                    };
                    break;
                }
            }

            _skillCache[guildId] = builder.ToImmutable();
        }

        return newState;
    }

    private string BuildSkillPreamble(ulong guildId, string prompt)
    {
        if (!_skillCache.TryGetValue(guildId, out var skills))
            return prompt;

        var enabled = skills.Where(static s => s.IsEnabled).ToList();
        if (enabled.Count == 0)
            return prompt;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[SERVER INSTRUCTIONS - You must follow these]");
        foreach (var skill in enabled)
            sb.AppendLine($"[{skill.Name}]: {skill.Instruction}");

        sb.AppendLine();
        sb.Append("User's message: ");
        sb.Append(prompt);
        return sb.ToString();
    }

    /// <summary>
    /// Checks if a user is allowed to use the AI agent.
    /// Owner is always allowed. When patronage is enabled, active patrons are also allowed.
    /// Results are cached for 1 minute to avoid DB queries on every message.
    /// </summary>
    public async Task<bool> IsAllowedAsync(IUser user)
    {
        if (credsProvider.GetCreds().IsOwner(user))
            return true;

        if (!patronageConfig.Data.IsEnabled)
            return false;

        var now = DateTime.UtcNow;
        if (_allowedCache.TryGetValue(user.Id, out var cached) && cached.ExpiresUtc > now)
            return cached.Allowed;

        var patron = await patronageService.GetPatronAsync(user.Id);
        var allowed = patron is { IsActive: true };
        _allowedCache[user.Id] = (allowed, now.AddMinutes(1));
        return allowed;
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
