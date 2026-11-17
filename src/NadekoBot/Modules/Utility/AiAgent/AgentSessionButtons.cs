using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Threading.Channels;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Permissions.Services;

namespace NadekoBot.Modules.Utility.AiAgent;

// The button id names the member, so one handler on each shard answers it, also on a message from before a restart.
public sealed class AgentSessionButtons(
    DiscordSocketClient client,
    AiAgentService agent,
    ConversationWindowTracker sessions,
    AgentLimiter limiter,
    AiAgentConfigService configService,
    IBotStrings strings,
    IBotCache cache,
    BlacklistService blacklist) : INService, IReadyExecutor
{
    public const string BUTTON_PREFIX = "n:agentend:";

    private const int QUEUE_SIZE = 256;
    private const int WORKERS = 2;

    // A removal can wait 10 s for the budget of one server, so a second worker keeps the other servers going.
    private const int REMOVAL_WORKERS = 2;
    private const long DROP_LOG_INTERVAL_MS = 10_000;
    private static readonly TimeSpan _expiryCheck = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _cooldown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _maxWait = TimeSpan.FromSeconds(2.5);

    private static readonly MessageComponent _noComponents = new ComponentBuilder().Build();

    private readonly Channel<QueuedClick> _clicks = Channel.CreateBounded<QueuedClick>(new BoundedChannelOptions(QUEUE_SIZE)
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
            _ = Task.Run(ClickLoopInternalAsync);

        for (var i = 0; i < REMOVAL_WORKERS; i++)
            _ = Task.Run(RemoveLoopInternalAsync);

        _ = Task.Run(ExpiryLoopInternalAsync);
        return Task.CompletedTask;
    }

    public static MessageComponent Create(string label, ulong userId)
        => new ComponentBuilder()
           .WithButton(label, BUTTON_PREFIX + userId.ToString(CultureInfo.InvariantCulture), ButtonStyle.Secondary)
           .Build();

    private Task OnInteractionInternal(SocketInteraction interaction)
    {
        if (interaction is not SocketMessageComponent smc
            || !smc.Data.CustomId.StartsWith(BUTTON_PREFIX, StringComparison.Ordinal))
            return Task.CompletedTask;

        var userId = smc.User.Id;
        if (blacklist.IsBlacklisted(smc.GuildId, smc.ChannelId, userId)
            || !_waiting.TryAdd(userId, true))
            return Task.CompletedTask;

        if (_clicks.Writer.TryWrite(new(smc, Stopwatch.GetTimestamp())))
            return Task.CompletedTask;

        _waiting.TryRemove(userId, out _);
        LogDropInternal();
        return Task.CompletedTask;
    }

    private void LogDropInternal()
    {
        Interlocked.Increment(ref _dropped);

        var now = Environment.TickCount64;
        var last = Volatile.Read(ref _lastDropLog);
        if (now - last < DROP_LOG_INTERVAL_MS
            || Interlocked.CompareExchange(ref _lastDropLog, now, last) != last)
            return;

        Log.Warning("The End session click queue is full, so the bot dropped {Count} clicks", Interlocked.Exchange(ref _dropped, 0));
    }

    private async Task ClickLoopInternalAsync()
    {
        await foreach (var click in _clicks.Reader.ReadAllAsync())
        {
            var userId = click.Interaction.User.Id;
            try
            {
                await AnswerInternalAsync(click);
            }
            catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownInteraction)
            {
                Log.Debug("The End session click of {UserId} expired before the bot answered", userId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not answer the End session click of {UserId}", userId);
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
        if (!ulong.TryParse(
                smc.Data.CustomId.AsSpan(BUTTON_PREFIX.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var ownerId))
            return;

        if (smc.User.Id != ownerId)
        {
            if (await cache.AddAsync(CooldownKey(smc.User.Id), true, expiry: _cooldown, overwrite: false))
                await smc.RespondAsync(
                    strings.GetText(strs.agent_session_other, smc.GuildId),
                    ephemeral: true,
                    allowedMentions: AllowedMentions.None);

            return;
        }

        // Also a button of a session which already ended goes away with the click.
        agent.CancelSession(ownerId, smc.Message.Id);
        try
        {
            await smc.UpdateAsync(static p => p.Components = _noComponents);
        }
        catch when (smc.GuildId is { } guildId)
        {
            // The session is closed, so the worker removes the button which the click could not.
            sessions.QueueRemoval(new(guildId, smc.ChannelId ?? smc.Message.Channel.Id, smc.Message.Id));
            throw;
        }
    }

    private async Task RemoveLoopInternalAsync()
    {
        await foreach (var button in sessions.Stale.ReadAllAsync())
        {
            try
            {
                await RemoveInternalAsync(button);
            }
            // 400 is an archived thread which the cache did not know about.
            catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                Log.Debug("Could not remove an End session button in channel {ChannelId}: {Error}", button.ChannelId, ex.Message);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not remove an End session button in channel {ChannelId}", button.ChannelId);
            }
        }
    }

    // A button which waits too long for the budget of the server stays, and a click removes it.
    private async Task RemoveInternalAsync(SessionButton button)
    {
        if (client.GetChannel(button.ChannelId) is not SocketTextChannel channel
            || channel is SocketThreadChannel { IsArchived: true }
            || channel.Guild.CurrentUser is not { } bot
            || AgentPermissions.IsTimedOut(bot)
            || !AgentPermissions.PermissionsIn(bot, channel).ViewChannel)
            return;

        if (!await limiter.TakeRestAsync(button.GuildId, 1, CancellationToken.None))
            return;

        await channel.ModifyMessageAsync(button.MessageId, static p => p.Components = _noComponents);
    }

    private async Task ExpiryLoopInternalAsync()
    {
        Func<ulong, bool> hasRequest = agent.HasRequest;
        using var timer = new PeriodicTimer(_expiryCheck);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                sessions.CloseExpired(configService.Data.FollowUpWindowSeconds, hasRequest);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in the AI agent session expiry loop");
            }
        }
    }

    private static TypedKey<bool> CooldownKey(ulong userId)
        => new($"agent:endsession:{userId}");

    private readonly record struct QueuedClick(SocketMessageComponent Interaction, long QueuedAt);
}
