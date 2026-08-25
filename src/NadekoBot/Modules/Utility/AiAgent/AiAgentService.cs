using System.Collections.Immutable;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration;
using NadekoBot.Modules.Patronage;

namespace NadekoBot.Modules.Utility.AiAgent;

public sealed class AiAgentService(
    IAiAgentSession agentSession,
    IAiToolRegistry toolRegistry,
    AiAgentConfigService configService,
    EmbeddingService embedder,
    CommandSearchService searchService,
    ConversationWindowTracker conversationTracker,
    AiAgentWhitelistService whitelist,
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
    private readonly ConcurrentDictionary<ulong, (bool Allowed, DateTime ExpiresUtc)> _patronCache = new();
    private readonly ConcurrentDictionary<ulong, ImmutableArray<AiAgentGuildSkill>> _skillCache = new();

    private sealed record QueuedMessage(IGuild Guild, ITextChannel Channel, IUserMessage Message, string Text);

    public const int MAX_SKILLS_PER_GUILD = 10;
    public const int MAX_SKILL_INSTRUCTION_LENGTH = 2000;
    public const int MAX_SKILL_NAME_LENGTH = 50;
    private const string BOT_TOKEN = "<bot>";
    private static readonly string[] _namePrefixes = ["hey", "hi", "yo", "ok", "dear"];
    private bool _credsWarningLogged;

    // Built once, because the id of the bot cannot change while it runs.
    private string? _normalMention;
    private string? _nickMention;

    // Higher than the other handlers, so the agent takes precedence when it is enabled.
    public int Priority
        => 3;

    // off if config disables it, or if the embedding runtime failed to load this session
    private bool IsAiEnabled
        => configService.Data.Enabled && !embedder.IsUnavailable;

    public async Task OnReadyAsync()
    {
        await LoadSkillCacheAsync();
        client.MessageReceived += OnMessageReceivedFeederAsync;
        client.MessageDeleted += OnMessageDeletedFeederAsync;
        _ = Task.Run(RunMemoryExpiryLoopAsync);
    }

    // Runs for every message, whoever the author is, so the agent sees its own output between turns.
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

    // Keeps channel_history in line with what the channel still holds.
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
                foreach (var (userId, entry) in _patronCache)
                {
                    if (entry.ExpiresUtc <= now)
                        _patronCache.TryRemove(userId, out _);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in agent memory expiry loop");
            }
        }
    }

    // Runs before the command parsing, so an explicit mention always takes priority.
    public async ValueTask<bool> ExecOnMessageAsync(IGuild? guild, IUserMessage msg)
    {
        if (!IsAiEnabled)
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

        var normalMention = _normalMention ??= $"<@{nadekoId}>";
        var nickMention = _nickMention ??= $"<@!{nadekoId}>";

        var content = msg.Content.AsSpan();
        ReadOnlySpan<char> rest;

        if (content.StartsWith(normalMention, StringComparison.InvariantCulture))
            rest = content[normalMention.Length..];
        else if (content.StartsWith(nickMention, StringComparison.InvariantCulture))
            rest = content[nickMention.Length..];
        else
            return false;

        rest = rest.Trim();

        if (rest.IsEmpty)
            return false;

        var query = new string(rest);

        if (!await IsAllowedAsync(msg.Author, guild))
            return false;

        return await TryRunAgentAsync(guild, channel, msg, query);
    }

    // Runs only when no command matched, so a prefixed command is never intercepted.
    public async ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (!IsAiEnabled)
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
                if (!await IsAllowedAsync(msg.Author, guild))
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
                if (!await IsAllowedAsync(msg.Author, guild))
                    return;

                await TryRunAgentAsync(guild, channel, msg, query);
                return;
            }
        }

        if (config.NameTriggerEnabled && searchService.IsReady && guild is SocketGuild sg)
        {
            var matchedName = MatchBotName(msg.Content,
                sg.CurrentUser?.Nickname,
                sg.CurrentUser?.DisplayName,
                client.CurrentUser.Username);

            if (matchedName is not null)
            {
                var normalized = NormalizeBotName(msg.Content, matchedName);
                if (!string.IsNullOrWhiteSpace(normalized) && searchService.IsCommandIntent(normalized))
                {
                    if (!await IsAllowedAsync(msg.Author, guild))
                        return;

                    var query = StripBotName(msg.Content, matchedName).Trim();
                    await TryRunAgentAsync(guild, channel, msg, query);
                    return;
                }
            }
        }
    }

    // The first name the message holds. A duplicate name matches the same span, so it is not filtered out.
    private static string? MatchBotName(string content, string? nickname, string? displayName, string? username)
    {
        if (!string.IsNullOrWhiteSpace(nickname) && content.Contains(nickname, StringComparison.OrdinalIgnoreCase))
            return nickname;

        if (!string.IsNullOrWhiteSpace(displayName)
            && content.Contains(displayName, StringComparison.OrdinalIgnoreCase))
            return displayName;

        if (!string.IsNullOrWhiteSpace(username) && content.Contains(username, StringComparison.OrdinalIgnoreCase))
            return username;

        return null;
    }

    // Keeps the grammar, so "how much money does BotName have" does not lose the subject.
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

    public async Task<bool> TryRunAgentAsync(
        IGuild guild,
        ITextChannel channel,
        IUserMessage message,
        string prompt)
    {
        if (!IsAiEnabled)
            return false;

        var config = configService.Data;

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
            var enrichedPrompt = BuildSkillPreamble(guild.Id, channel.Id, prompt);
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

                    if (smart is SmartEmbedTextArray)
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

    // The backfill is capped by what one Discord call returns. Live events fill the rest.
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

    private static MessageSnapshot CreateSnapshot(IMessage msg)
        => new(
            msg.Id,
            msg.Author.Id,
            PromptSanitizer.Sanitize(msg.Author.Username),
            PromptSanitizer.Sanitize(GetMessageText(msg)),
            msg.Timestamp);

    // Labeled sections, so the LLM can read a message next to the other history entries.
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

    // A null channelId scopes the skill to the whole guild.
    public async Task<bool> AddSkillAsync(ulong guildId, string name, string instruction, ulong? channelId = null)
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

        _skillCache[guildId] = skills.Add(new()
        {
            Id = id,
            GuildId = guildId,
            ChannelId = channelId,
            Name = name,
            Instruction = instruction,
            IsEnabled = true
        });

        return true;
    }

    public async Task<bool> RemoveSkillAsync(ulong guildId, string name, ulong? channelId = null)
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

    public async Task<bool?> ToggleSkillAsync(ulong guildId, string name, ulong? channelId = null)
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
                var skill = builder[i];
                if (skill.Name != name || skill.ChannelId != channelId)
                    continue;

                builder[i] = new()
                {
                    Id = skill.Id,
                    GuildId = guildId,
                    ChannelId = channelId,
                    Name = name,
                    Instruction = skill.Instruction,
                    IsEnabled = newState
                };
                break;
            }

            _skillCache[guildId] = builder.ToImmutable();
        }

        return newState;
    }

    public IReadOnlyList<AiAgentGuildSkill> GetSkills(ulong guildId, ulong? channelId = null)
    {
        if (!_skillCache.TryGetValue(guildId, out var skills))
            return [];

        var result = ImmutableArray.CreateBuilder<AiAgentGuildSkill>();
        foreach (var skill in skills)
        {
            if (skill.ChannelId == channelId)
                result.Add(skill);
        }

        return result.ToImmutable();
    }

    // A skill bound to another channel is left out.
    private string BuildSkillPreamble(ulong guildId, ulong channelId, string prompt)
    {
        if (!_skillCache.TryGetValue(guildId, out var skills))
            return prompt;

        var sb = new System.Text.StringBuilder();
        foreach (var skill in skills)
        {
            if (!skill.IsEnabled)
                continue;

            if (skill.ChannelId is not null && skill.ChannelId != channelId)
                continue;

            sb.Append('[').Append(skill.Name).Append("]: ").AppendLine(skill.Instruction);
        }

        if (sb.Length == 0)
            return prompt;

        sb.Insert(0, "[SERVER INSTRUCTIONS - You must follow these]\n");
        sb.AppendLine();
        sb.Append("User's message: ");
        sb.Append(prompt);
        return sb.ToString();
    }

    // Only the patron lookup hits the database, and it is cached to keep it off the message path.
    public async Task<bool> IsAllowedAsync(IUser user, IGuild? guild = null)
    {
        if (credsProvider.GetCreds().IsOwner(user))
            return true;

        if (whitelist.IsWhitelisted(AiAgentWhitelistType.User, user.Id))
            return true;

        if (guild is not null && whitelist.IsWhitelisted(AiAgentWhitelistType.Server, guild.Id))
            return true;

        if (guild is not null && whitelist.GetSet(AiAgentWhitelistType.Role).Count > 0)
        {
            var guildUser = user as IGuildUser ?? await guild.GetUserAsync(user.Id);
            if (guildUser is not null
                && whitelist.IsAnyWhitelisted(AiAgentWhitelistType.Role, guildUser.RoleIds))
                return true;
        }

        if (!patronageConfig.Data.IsEnabled)
            return false;

        var now = DateTime.UtcNow;
        if (_patronCache.TryGetValue(user.Id, out var cached) && cached.ExpiresUtc > now)
            return cached.Allowed;

        var patron = await patronageService.GetPatronAsync(user.Id);
        var allowed = patron is { IsActive: true };
        _patronCache[user.Id] = (allowed, now.AddMinutes(1));
        return allowed;
    }

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
}
