using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Gambling.Blackjack;

public sealed class BlackjackService(
    DiscordSocketClient client,
    BlackjackLedger ledger,
    BlackjackRenderer renderer,
    ShardData shardData,
    TimeProvider? timeProvider = null) : INService, IReadyExecutor
{
    public const int MAX_TABLES_PER_SERVER = 3;
    public const int RESTART_REFUND_ATTEMPTS = 5;
    public static readonly TimeSpan RestartRefundRetryDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan StaleStakeCheckInterval = TimeSpan.FromMinutes(5);

    // stakes of a member without a seat which are this old belong to a round whose payout failed
    public static readonly TimeSpan StaleStakeAge = TimeSpan.FromMinutes(10);
    private const int JOIN_ATTEMPTS = 2;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // every stake of this process is newer than this, so older stakes belong to rounds of a dead process
    private readonly DateTime _startedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private readonly ConcurrentDictionary<ulong, BlackjackTable> _tables = new();
    private readonly ConcurrentHashSet<ulong> _seated = new();

    // open tables per server, changed only under the lock
    private readonly Dictionary<ulong, int> _tablesPerGuild = new();
    private readonly Lock _openLock = new();
    private Func<ulong, bool>? _isSeated;
    private int _subscribed;

    // a join waits while the rounds of a dead process settle, so a new table never takes their stakes
    private int _settlingRounds;

    private Func<ulong, bool> IsSeatedCheck
        => _isSeated ??= IsSeated;

    public Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
        {
            client.InteractionCreated += OnInteractionCreatedInternal;
            client.MessageReceived += OnMessageReceivedInternal;
            _ = Task.Run(StaleStakeLoopInternalAsync);
        }

        _ = Task.Run(RefundStaleInternalAsync);
        return Task.CompletedTask;
    }

    private async Task StaleStakeLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(StaleStakeCheckInterval, _time);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                await SettleRoundsInternalAsync();

                var before = (_time.GetUtcNow() - StaleStakeAge).UtcDateTime;
                var refunded = await ledger.RefundStaleAsync(shardData.TotalShards,
                    shardData.ShardId,
                    before,
                    BjRefundReason.Unpaid,
                    IsSeatedCheck);

                if (refunded > 0)
                    Log.Information("Blackjack refunded {Count} stakes of rounds which the bot could not pay out", refunded);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Blackjack could not refund the stakes of rounds which the bot could not pay out");
            }
        }
    }

    private async Task RefundStaleInternalAsync()
    {
        for (var attempt = 1; attempt <= RESTART_REFUND_ATTEMPTS; attempt++)
        {
            try
            {
                await SettleRoundsInternalAsync();

                var refunded = await ledger.RefundStaleAsync(shardData.TotalShards,
                    shardData.ShardId,
                    _startedAt,
                    BjRefundReason.Restart,
                    IsSeatedCheck);
                if (refunded > 0)
                    Log.Information("Blackjack refunded {Count} stakes of seats which the restart interrupted before the deal",
                        refunded);
                return;
            }
            catch (Exception ex) when (attempt < RESTART_REFUND_ATTEMPTS)
            {
                Log.Warning(ex, "Blackjack could not refund the stakes of unfinished rounds, try {Attempt}", attempt);
                await Task.Delay(RestartRefundRetryDelay, _time);
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "Blackjack could not refund the stakes of unfinished rounds. The bot tries again after the next restart");
            }
        }
    }

    // Finishes the rounds of a process which died: every open hand stands, the dealer plays from the saved shoe,
    // and the round pays as it would have. A round which cannot be finished keeps its stakes for the next try.
    private async Task SettleRoundsInternalAsync()
    {
        if (Interlocked.Exchange(ref _settlingRounds, 1) == 1)
            return;

        try
        {
            var states = await ledger.GetRoundStatesAsync(shardData.TotalShards, shardData.ShardId);
            var settled = 0;

            foreach (var state in states)
            {
                // a table which runs in this process holds its own round; only a round of a dead process is settled
                if (_tables.ContainsKey(state.ChannelId))
                    continue;

                try
                {
                    if (BlackjackRoundStateCodec.Read(state.Cards) is not { } round)
                    {
                        Log.Error(
                            "Blackjack could not read the saved round in channel {ChannelId}. The bot refunds its stakes",
                            state.ChannelId);
                        await ledger.DeleteRoundAsync(state.GuildId, state.ChannelId);
                        continue;
                    }

                    try
                    {
                        round.FinishRestored();
                    }
                    catch (Exception ex)
                    {
                        // A row which cannot be played out reads the same on every try, so its stakes go back.
                        Log.Error(ex,
                            "Blackjack could not play out the saved round in channel {ChannelId}. "
                            + "The bot refunds its stakes",
                            state.ChannelId);
                        await ledger.DeleteRoundAsync(state.GuildId, state.ChannelId);
                        continue;
                    }

                    var settlements = new BjSettlement[round.Players.Length];
                    for (var i = 0; i < settlements.Length; i++)
                    {
                        var player = round.Players[i];
                        settlements[i] = new(player.UserId, player.TotalStake, BlackjackRound.GetPayouts(player));
                    }

                    if (await ledger.SettleRoundAsync(state.GuildId, state.ChannelId, settlements) > 0)
                        settled++;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Blackjack could not finish the saved round in channel {ChannelId}", state.ChannelId);
                }
            }

            if (settled > 0)
                Log.Information("Blackjack finished {Count} rounds which a restart interrupted", settled);
        }
        finally
        {
            Volatile.Write(ref _settlingRounds, 0);
        }
    }

    public async Task<BjJoinResult> JoinAsync(ulong guildId, ulong channelId, ulong userId, long bet, string prefix)
    {
        if (Volatile.Read(ref _settlingRounds) != 0)
            return BjJoinResult.Busy;

        if (_seated.Contains(userId))
            return BjJoinResult.AlreadySeated;

        // a table which closes while the join waits in its queue answers Closed, then the member opens a new one
        for (var attempt = 0; attempt < JOIN_ATTEMPTS; attempt++)
        {
            if (GetOrOpenTableInternal(guildId, channelId, prefix) is not { } table)
                return BjJoinResult.TooManyTables;

            var result = await table.JoinAsync(userId, bet);
            if (result != BjJoinResult.Closed)
                return result;
        }

        return BjJoinResult.Closed;
    }

    public bool TryReserveSeat(ulong userId)
        => _seated.Add(userId);

    public void ReleaseSeat(ulong userId)
        => _seated.TryRemove(userId);

    public bool IsSeated(ulong userId)
        => _seated.Contains(userId);

    public void OnTableClosed(BlackjackTable table)
    {
        lock (_openLock)
        {
            // a new table of the same channel can already be in the list
            if (!_tables.TryGetValue(table.ChannelId, out var current)
                || !ReferenceEquals(current, table)
                || !_tables.TryRemove(table.ChannelId, out _))
                return;

            if (_tablesPerGuild.TryGetValue(table.GuildId, out var count) && count > 1)
                _tablesPerGuild[table.GuildId] = count - 1;
            else
                _tablesPerGuild.Remove(table.GuildId);
        }
    }

    private BlackjackTable? GetOrOpenTableInternal(ulong guildId, ulong channelId, string prefix)
    {
        if (_tables.TryGetValue(channelId, out var table))
            return table;

        lock (_openLock)
        {
            if (_tables.TryGetValue(channelId, out table))
                return table;

            var count = _tablesPerGuild.GetValueOrDefault(guildId);
            if (count >= MAX_TABLES_PER_SERVER)
                return null;

            table = new BlackjackTable(this, ledger, renderer, client, _time, guildId, channelId, prefix);
            _tables[channelId] = table;
            _tablesPerGuild[guildId] = count + 1;
        }

        _ = Task.Run(table.RunAsync);
        return table;
    }

    // Gateway handler for every message: one lookup and one increment, so a busy channel costs nothing more.
    private Task OnMessageReceivedInternal(SocketMessage message)
    {
        if (_tables.TryGetValue(message.Channel.Id, out var table))
            table.OnChannelMessage(message.Id);

        return Task.CompletedTask;
    }

    // Gateway handler: no I/O and no task per click. The table queues the click for its own loop.
    private Task OnInteractionCreatedInternal(SocketInteraction interaction)
    {
        if (interaction is not SocketMessageComponent smc
            || !smc.Data.CustomId.StartsWith(BlackjackTable.ID_PREFIX, StringComparison.Ordinal)
            || smc.ChannelId is not { } channelId
            || !_tables.TryGetValue(channelId, out var table))
            return Task.CompletedTask;

        table.TryQueueClick(smc);
        return Task.CompletedTask;
    }
}
