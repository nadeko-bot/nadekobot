using System.Diagnostics;
using System.Threading.Channels;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Permissions.Services;

namespace NadekoBot.Modules.Games.Quests;

public sealed class QuestLogService(
    QuestService quests,
    DiscordSocketClient client,
    IBotStrings strings,
    IMessageSenderService sender,
    IBotCache cache,
    BlacklistService blacklist
) : INService, IReadyExecutor
{
    public const string BUTTON_ID = "n:quests";

    private const int QUEUE_SIZE = 256;
    private const int WORKERS = 2;
    private const long DROP_LOG_INTERVAL_MS = 10_000;
    private static readonly TimeSpan _cooldown = TimeSpan.FromSeconds(3);

    // Discord accepts an answer only in the first 3 seconds after the click.
    private static readonly TimeSpan _maxWait = TimeSpan.FromSeconds(2.5);

    private static readonly IEmote _emote = Emoji.Parse("📜");

    private readonly Channel<QueuedClick> _queue = Channel.CreateBounded<QueuedClick>(new BoundedChannelOptions(QUEUE_SIZE)
    {
        SingleReader = false,
        SingleWriter = false
    });

    private readonly ConcurrentDictionary<ulong, bool> _waiting = new();
    private int _started;
    private long _dropped;
    private long _lastDropLog;

    public Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return Task.CompletedTask;

        client.InteractionCreated += OnInteractionInternal;
        for (var i = 0; i < WORKERS; i++)
            _ = Task.Run(WorkerLoopInternalAsync);

        return Task.CompletedTask;
    }

    public ButtonBuilder CreateButton(ulong? guildId)
        => CreateButtonWithLabel(GetText(strs.daily_quests, guildId));

    public static ButtonBuilder CreateButtonWithLabel(string label)
        => new(label: label, customId: BUTTON_ID, style: ButtonStyle.Secondary, emote: _emote);

    public async Task<EmbedBuilder> BuildEmbedAsync(ulong userId, ulong? guildId)
    {
        var now = DateTime.UtcNow;
        var userQuests = await quests.GetUserQuestsAsync(userId, now);

        var allDone = true;
        foreach (var (_, userQuest) in userQuests)
            allDone &= userQuest.IsCompleted;

        var reset = TimestampTag.FromDateTime(now.AddDays(1).Date, TimestampTagStyles.Relative);
        var desc = GetText(strs.dailies_reset(reset), guildId);
        if (allDone)
            desc = GetText(strs.dailies_done, guildId) + "\n" + desc;

        var embed = sender.CreateEmbed(guildId)
            .WithOkColor()
            .WithTitle(GetText(strs.quest_log, guildId))
            .WithDescription(desc);

        foreach (var (quest, userQuest) in userQuests)
        {
            if (quest is null)
                continue;

            embed.AddField(
                (userQuest.IsCompleted ? IQuest.COMPLETED : IQuest.INCOMPLETE) + " " + quest.Name,
                $"{quest.Desc}\n\n{quest.ToString(userQuest.Progress)}",
                true);
        }

        return embed;
    }

    private Task OnInteractionInternal(SocketInteraction interaction)
    {
        if (interaction is not SocketMessageComponent smc
            || !string.Equals(smc.Data.CustomId, BUTTON_ID, StringComparison.Ordinal))
            return Task.CompletedTask;

        var userId = smc.User.Id;
        if (blacklist.IsBlacklisted(smc.GuildId, smc.ChannelId, userId)
            || !_waiting.TryAdd(userId, true))
            return Task.CompletedTask;

        if (_queue.Writer.TryWrite(new(smc, Stopwatch.GetTimestamp())))
            return Task.CompletedTask;

        _waiting.TryRemove(userId, out _);
        LogDropInternal();
        return Task.CompletedTask;
    }

    // A burst can fill the queue with thousands of members, so the warning comes at most once in each interval.
    private void LogDropInternal()
    {
        Interlocked.Increment(ref _dropped);

        var now = Environment.TickCount64;
        var last = Volatile.Read(ref _lastDropLog);
        if (now - last < DROP_LOG_INTERVAL_MS
            || Interlocked.CompareExchange(ref _lastDropLog, now, last) != last)
            return;

        Log.Warning("The quest log queue is full, so the bot dropped {Count} clicks", Interlocked.Exchange(ref _dropped, 0));
    }

    private async Task WorkerLoopInternalAsync()
    {
        await foreach (var click in _queue.Reader.ReadAllAsync())
        {
            var userId = click.Interaction.User.Id;
            try
            {
                await AnswerInternalAsync(click);
            }
            catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownInteraction)
            {
                Log.Debug("The quest log click of {UserId} expired before the bot answered", userId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not show the quest log to {UserId}", userId);
            }
            finally
            {
                _waiting.TryRemove(userId, out _);
            }
        }
    }

    private async Task AnswerInternalAsync(QueuedClick click)
    {
        if (Stopwatch.GetElapsedTime(click.QueuedAt) > _maxWait)
            return;

        var smc = click.Interaction;
        if (!await cache.AddAsync(CooldownKey(smc.User.Id), true, expiry: _cooldown, overwrite: false))
            return;

        // The defer answers in time, and the follow-up has 15 minutes, so a slow database read does not expire the click.
        await smc.DeferAsync(ephemeral: true);
        var embed = await BuildEmbedAsync(smc.User.Id, smc.GuildId);
        await smc.FollowupAsync(embed: embed.Build(), ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private string GetText(LocStr str, ulong? guildId)
        => strings.GetText(str.Key, guildId, str.Params);

    private static TypedKey<bool> CooldownKey(ulong userId)
        => new($"quests:log:{userId}");

    private readonly record struct QueuedClick(SocketMessageComponent Interaction, long QueuedAt);
}
