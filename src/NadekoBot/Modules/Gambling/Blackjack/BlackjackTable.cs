using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace NadekoBot.Modules.Gambling.Blackjack;

public enum BjJoinResult : byte
{
    Opened,
    Joined,
    AlreadySeated,
    NotEnough,
    Full,
    Started,
    TooManyTables,
    Busy,
    Closed
}

public enum BjTablePhase : byte
{
    Joining,
    Playing,
    Finished,
    Closed
}

// One table runs on one loop, which handles joins, clicks and timeouts one after another.
// Thus a double click, a click during a payout or a timeout during a click cannot change the round twice.
public sealed class BlackjackTable(
    BlackjackService service,
    BlackjackLedger ledger,
    BlackjackRenderer renderer,
    DiscordSocketClient client,
    TimeProvider time,
    ulong guildId,
    ulong channelId,
    string prefix)
{
    public const string ID_PREFIX = "bj:";
    public const int MAX_SEATS = 5;

    public const char ACT_START = 'g';
    public const char ACT_HIT = 'h';
    public const char ACT_STAND = 's';
    public const char ACT_DOUBLE = 'd';
    public const char ACT_SPLIT = 'p';

    public static readonly TimeSpan JoinWindow = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan DecisionTime = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan EditInterval = TimeSpan.FromMilliseconds(1500);

    // the dealer's cards appear one per edit after the payout, with a pause which leaves an edit time to land
    public static readonly TimeSpan RevealStep = TimeSpan.FromSeconds(2);

    // how often an idle table checks if it must move to the bottom of the chat
    public static readonly TimeSpan RepostCheckInterval = TimeSpan.FromSeconds(5);

    public const int MAX_NAME_LENGTH = 20;

    private const int INBOX_CAPACITY = 16;
    private const int MAX_REPOSTS = 2;
    private const int MAX_EDIT_FAILURES = 5;
    private const int SETTLE_ATTEMPTS = 3;
    private static readonly TimeSpan _settleRetryDelay = TimeSpan.FromSeconds(2);

    // a member gets at most one answer per this time for clicks which change nothing
    public static readonly TimeSpan QuietAnswerCooldown = TimeSpan.FromMilliseconds(2500);

    private static readonly NadekoRandom _rng = new();

    private readonly Channel<TableEvent> _inbox = Channel.CreateBounded<TableEvent>(
        new BoundedChannelOptions(INBOX_CAPACITY)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

    // members with a click in the inbox: more clicks from them are dropped until the loop takes it
    private readonly ConcurrentDictionary<ulong, bool> _pendingClicks = new();
    private readonly List<BjSeat> _seats = new(MAX_SEATS);

    // display names from the gateway cache, by seat; null when the member was not cached
    private readonly List<string?> _names = new(MAX_SEATS);
    private readonly BlackjackRepostGate _repostGate = new(time);

    // only the loop reads and writes it
    private readonly Dictionary<ulong, long> _lastQuietAnswer = new(MAX_SEATS);

    // read by the gateway thread, replaced by the loop
    private volatile ulong[] _seatIds = [];
    private volatile BjTablePhase _phase = BjTablePhase.Joining;

    // writes an empty event into the inbox when the loop must wake up, so a timeout needs no exception
    private ITimer? _wakeTimer;

    // the click which the loop handles now, and whether it got DeferAsync before work on the database
    private SocketMessageComponent? _activeClick;
    private bool _clickDeferred;

    private IUserMessage? _message;
    private bool _dirty;
    private bool _noEdits;
    private DateTimeOffset _lastEdit;
    private int _reposts;
    private int _editFailures;
    private bool _paid;
    private int _dealerShown = int.MaxValue;

    public ulong GuildId { get; } = guildId;
    public ulong ChannelId { get; } = channelId;
    private readonly string _commandCode = Format.Code(prefix + BlackjackRenderer.COMMAND_SYNTAX);
    public BlackjackRound? Round { get; private set; }
    public DateTimeOffset Deadline { get; private set; } = time.GetUtcNow() + JoinWindow;
    public bool Stopped { get; private set; }
    public int Version { get; private set; }

    public BjTablePhase Phase
        => _phase;

    public IReadOnlyList<BjSeat> Seats
        => _seats;

    public ulong HostId
        => _seats.Count > 0 ? _seats[0].UserId : 0;

    private BjRenderState RenderState
        => new(GuildId, _commandCode, _phase, Stopped, Round, _seats, _names, Deadline, Version, _dealerShown);

    private bool IsRepostDue
        => _phase is BjTablePhase.Joining or BjTablePhase.Playing
           && _message is not null
           && !_noEdits
           && time.GetUtcNow() >= _lastEdit + EditInterval
           && _repostGate.IsDue;

    public static string ButtonId(char action, int version)
        => string.Create(CultureInfo.InvariantCulture, $"{ID_PREFIX}{action}:{version}");

    public static bool TryParseButtonId(string customId, out char action, out int version)
    {
        action = default;
        version = default;

        var id = customId.AsSpan();
        if (id.Length < ID_PREFIX.Length + 3
            || !id.StartsWith(ID_PREFIX, StringComparison.Ordinal)
            || id[ID_PREFIX.Length + 1] != ':')
            return false;

        action = id[ID_PREFIX.Length];
        return int.TryParse(id[(ID_PREFIX.Length + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }

    public Task<BjJoinResult> JoinAsync(ulong userId, long bet)
    {
        var reply = new TaskCompletionSource<BjJoinResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_inbox.Writer.TryWrite(new(userId, bet, null, reply)))
            return reply.Task;

        return Task.FromResult(_phase == BjTablePhase.Closed ? BjJoinResult.Closed : BjJoinResult.Busy);
    }

    // Runs on the gateway thread for every message in the channel: one comparison and one increment.
    public void OnChannelMessage(ulong messageId)
        => _repostGate.OnMessage(messageId);

    // Runs on the gateway thread: no I/O and no task. A member who does not sit here costs nothing.
    public void TryQueueClick(SocketMessageComponent smc)
    {
        var userId = smc.User.Id;
        if (Array.IndexOf(_seatIds, userId) < 0)
            return;

        if (!_pendingClicks.TryAdd(userId, true))
            return;

        if (_inbox.Writer.TryWrite(new(userId, 0, smc, null)))
            return;

        _pendingClicks.TryRemove(userId, out _);
        Log.Warning("Blackjack table in channel {ChannelId} dropped a click of {UserId}, because its queue is full",
            ChannelId,
            userId);
    }

    public async Task RunAsync()
    {
        _wakeTimer = time.CreateTimer(static state => ((BlackjackTable)state!).WakeInternal(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        try
        {
            await LoopInternalAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Blackjack table in channel {ChannelId} failed", ChannelId);
            try
            {
                _noEdits = true;
                await StopInternalAsync();
                DetachInternal();
                await ShowFinalInternalAsync();
            }
            catch (Exception stopEx)
            {
                Log.Error(stopEx, "Blackjack table in channel {ChannelId} could not refund the stakes", ChannelId);
            }
        }
        finally
        {
            DetachInternal();
            _wakeTimer.Dispose();
        }
    }

    private void WakeInternal()
        => _inbox.Writer.TryWrite(default);

    // The service forgets the table as soon as the round is over, before the last edit,
    // so a .bj in the channel opens a new table at once. Clicks on this message then find a table with another message.
    private void DetachInternal()
    {
        if (_phase == BjTablePhase.Closed)
            return;

        _phase = BjTablePhase.Closed;
        service.OnTableClosed(this);

        _inbox.Writer.TryComplete();
        while (_inbox.Reader.TryRead(out var ev))
            ev.Reply?.TrySetResult(BjJoinResult.Closed);

        ReleaseAllSeatsInternal();
    }

    private async Task LoopInternalAsync()
    {
        while (_phase is BjTablePhase.Joining or BjTablePhase.Playing)
        {
            var ev = await NextEventInternalAsync();
            if (!ev.IsWake)
                await HandleEventInternalAsync(ev);

            if (_phase is BjTablePhase.Joining or BjTablePhase.Playing && time.GetUtcNow() >= Deadline)
                await OnDeadlineInternalAsync();

            if (IsRepostDue || (_dirty && time.GetUtcNow() >= _lastEdit + EditInterval))
                await FlushInternalAsync();
        }

        DetachInternal();

        if (!_dirty || _noEdits)
            return;

        var wait = _lastEdit + EditInterval - time.GetUtcNow();
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, time);

        await FlushInternalAsync();
    }

    // A read without a token reuses the channel's single reader operation, so a wait allocates nothing.
    // A wake event left over from an earlier wait only makes the loop check its deadlines once more.
    private async ValueTask<TableEvent> NextEventInternalAsync()
    {
        if (_inbox.Reader.TryRead(out var ready))
            return ready;

        var now = time.GetUtcNow();
        var wakeAt = Deadline;
        if (_dirty && _lastEdit + EditInterval < wakeAt)
            wakeAt = _lastEdit + EditInterval;

        if (_message is not null && now + RepostCheckInterval < wakeAt)
            wakeAt = now + RepostCheckInterval;

        var delay = wakeAt - now;
        if (delay <= TimeSpan.Zero)
            return default;

        _wakeTimer!.Change(delay, Timeout.InfiniteTimeSpan);
        return await _inbox.Reader.ReadAsync();
    }

    private async Task HandleEventInternalAsync(TableEvent ev)
    {
        if (ev.Click is { } smc)
        {
            _pendingClicks.TryRemove(ev.UserId, out _);
            _activeClick = smc;
            _clickDeferred = false;
            try
            {
                await ClickInternalAsync(smc);
            }
            finally
            {
                _activeClick = null;
            }

            return;
        }

        var result = BjJoinResult.Busy;
        try
        {
            result = await JoinInternalAsync(ev.UserId, ev.Bet);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Blackjack could not seat {UserId} in channel {ChannelId}", ev.UserId, ChannelId);
        }

        ev.Reply?.TrySetResult(result);
    }

    private async Task<BjJoinResult> JoinInternalAsync(ulong userId, long bet)
    {
        if (_phase != BjTablePhase.Joining)
            return _phase == BjTablePhase.Playing ? BjJoinResult.Started : BjJoinResult.Closed;

        if (_seats.Count >= MAX_SEATS)
            return BjJoinResult.Full;

        if (!service.TryReserveSeat(userId))
            return BjJoinResult.AlreadySeated;

        BjSeatResult seated;
        try
        {
            seated = await ledger.SeatAsync(GuildId, ChannelId, userId, bet);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Blackjack could not seat {UserId} in channel {ChannelId}", userId, ChannelId);
            service.ReleaseSeat(userId);
            CloseIfEmptyInternal();
            return BjJoinResult.Busy;
        }

        if (seated != BjSeatResult.Seated)
        {
            service.ReleaseSeat(userId);
            CloseIfEmptyInternal();
            return seated == BjSeatResult.NotEnough ? BjJoinResult.NotEnough : BjJoinResult.AlreadySeated;
        }

        var opened = _message is null && _seats.Count == 0;
        _seats.Add(new(userId, bet));
        _names.Add(ResolveNameInternal(userId));
        PublishSeatsInternal();
        _dirty = true;

        if (opened)
        {
            Deadline = time.GetUtcNow() + JoinWindow;
            await FlushInternalAsync();

            // A table which can never show itself never starts, so the bet goes back instead of playing blind.
            if (_message is null && _noEdits)
            {
                await StopInternalAsync();
                DetachInternal();
                return BjJoinResult.Closed;
            }
        }

        if (_phase == BjTablePhase.Joining && _seats.Count == MAX_SEATS)
            await StartRoundInternalAsync();

        return opened ? BjJoinResult.Opened : BjJoinResult.Joined;
    }

    private void CloseIfEmptyInternal()
    {
        if (_seats.Count > 0 || _message is not null)
            return;

        _noEdits = true;
        _phase = BjTablePhase.Finished;
    }

    private async Task ClickInternalAsync(SocketMessageComponent smc)
    {
        if (_message is null
            || smc.Message.Id != _message.Id
            || !TryParseButtonId(smc.Data.CustomId, out var action, out var version))
        {
            if (TryTakeQuietAnswerInternal(smc.User.Id))
                await AckInternalAsync(smc);
            return;
        }

        ClickOutcome outcome;
        try
        {
            outcome = await ApplyClickInternalAsync(smc.User.Id, action, version);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Blackjack could not handle a click of {UserId} in channel {ChannelId}", smc.User.Id, ChannelId);
            outcome = default;
        }

        // a repost sends a new message, so the click must not edit the old one
        if (outcome.IsChanged && IsRepostDue)
        {
            await DeferActiveClickInternalAsync();
            await FlushInternalAsync();
            return;
        }

        if (_clickDeferred)
        {
            if (outcome.IsChanged)
                await ShowViaOriginalResponseInternalAsync(smc);
            else if (outcome.Refusal is { } late && TryTakeQuietAnswerInternal(smc.User.Id))
                await RefuseAfterDeferInternalAsync(smc, late);

            return;
        }

        if (outcome.IsChanged)
        {
            await UpdateViaInteractionInternalAsync(smc);
            return;
        }

        // A click which changes nothing gets no answer within the cooldown, so spam costs no Discord calls.
        if (!TryTakeQuietAnswerInternal(smc.User.Id))
            return;

        if (outcome.Refusal is { } refusal)
            await RefuseInternalAsync(smc, refusal);
        else
            await AckInternalAsync(smc);
    }

    // Discord drops a click without an answer after 3 seconds, so a click answers before it waits for the database.
    private async Task DeferActiveClickInternalAsync()
    {
        if (_activeClick is not { } smc || _clickDeferred)
            return;

        _clickDeferred = true;
        try
        {
            await smc.DeferAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Blackjack could not defer a click in channel {ChannelId}", ChannelId);
        }
    }

    private bool TryTakeQuietAnswerInternal(ulong userId)
    {
        var now = time.GetTimestamp();
        if (_lastQuietAnswer.TryGetValue(userId, out var last) && time.GetElapsedTime(last, now) < QuietAnswerCooldown)
            return false;

        _lastQuietAnswer[userId] = now;
        return true;
    }

    private async Task<ClickOutcome> ApplyClickInternalAsync(ulong userId, char action, int version)
    {
        if (_phase == BjTablePhase.Joining)
        {
            if (action != ACT_START)
                return default;

            if (userId != HostId)
                return ClickOutcome.Refuse(renderer.GetText(GuildId, strs.bj_only_host(MentionUtils.MentionUser(HostId))));

            await StartRoundInternalAsync();
            return ClickOutcome.Changed;
        }

        if (_phase != BjTablePhase.Playing || Round is not { } round)
            return default;

        if (round.Phase != BjRoundPhase.Players || version != Version)
            return default;

        if (round.CurrentPlayer!.UserId != userId)
            return ClickOutcome.Refuse(renderer.GetText(GuildId, strs.bj_not_your_turn));

        switch (action)
        {
            case ACT_HIT when round.CanHit:
                round.Hit();
                break;
            case ACT_STAND when round.CanHit:
                round.Stand();
                break;
            case ACT_DOUBLE when round.CanDouble:
                await DeferActiveClickInternalAsync();
                if (!await ledger.StakeAsync(GuildId, ChannelId, userId, round.DoubleCost, BjStakeKind.Double))
                    return ClickOutcome.Refuse(renderer.GetNotEnoughText(GuildId));

                round.Double();
                break;
            case ACT_SPLIT when round.CanSplit:
                await DeferActiveClickInternalAsync();
                if (!await ledger.StakeAsync(GuildId, ChannelId, userId, round.SplitCost, BjStakeKind.Split))
                    return ClickOutcome.Refuse(renderer.GetNotEnoughText(GuildId));

                round.Split();
                break;
            default:
                return default;
        }

        await AfterRoundStepInternalAsync(BjRoundPhase.Players);
        return ClickOutcome.Changed;
    }

    private async Task OnDeadlineInternalAsync()
    {
        if (_phase == BjTablePhase.Joining)
        {
            if (_seats.Count == 0)
            {
                _noEdits = true;
                _phase = BjTablePhase.Finished;
                return;
            }

            await StartRoundInternalAsync();
            return;
        }

        if (Round is not { } round)
            return;

        if (round.Phase == BjRoundPhase.Finished)
        {
            await RevealStepInternalAsync(round);
            return;
        }

        var before = round.Phase;
        round.Timeout();
        await AfterRoundStepInternalAsync(before);
    }

    // The money is paid before the reveal; the table stays in the Playing phase until the last dealer card shows.
    private void BeginRevealInternal(BlackjackRound round)
    {
        _dealerShown = 1;
        if (round.Dealer.Count <= _dealerShown)
        {
            _phase = BjTablePhase.Finished;
            return;
        }

        Deadline = time.GetUtcNow() + RevealStep;
    }

    private async Task RevealStepInternalAsync(BlackjackRound round)
    {
        _dealerShown++;
        _dirty = true;

        if (_dealerShown >= round.Dealer.Count)
            _phase = BjTablePhase.Finished;
        else
            Deadline = time.GetUtcNow() + RevealStep;

        await FlushInternalAsync();
    }

    private async Task StartRoundInternalAsync()
    {
        var round = new BlackjackRound(BlackjackShoe.CreateShuffled(_rng), CollectionsMarshal.AsSpan(_seats));
        Round = round;
        _phase = BjTablePhase.Playing;
        round.Deal();
        await AfterRoundStepInternalAsync(BjRoundPhase.Dealing);
    }

    // Each player decision and each phase change gives the buttons a new version, so an old click does nothing.
    private async Task AfterRoundStepInternalAsync(BjRoundPhase before)
    {
        var round = Round!;

        // A saved round lets a restart finish it instead of refunding it; a failed save only costs that.
        await SaveRoundInternalAsync(round);

        if (before == BjRoundPhase.Players || round.Phase != before)
        {
            Version++;
            Deadline = time.GetUtcNow() + DecisionTime;
        }

        _dirty = true;

        if (round.Phase != BjRoundPhase.Finished)
            return;

        await DeferActiveClickInternalAsync();
        await SettleInternalAsync();
        BeginRevealInternal(round);
    }

    private async Task SaveRoundInternalAsync(BlackjackRound round)
    {
        try
        {
            await ledger.SaveRoundAsync(GuildId, ChannelId, BlackjackRoundStateCodec.Write(round));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Blackjack could not save the round in channel {ChannelId}", ChannelId);
        }
    }

    private async Task SettleInternalAsync()
    {
        var round = Round!;

        var players = new BjSettlement[round.Players.Length];
        for (var i = 0; i < players.Length; i++)
        {
            var player = round.Players[i];
            players[i] = new(player.UserId, player.TotalStake, BlackjackRound.GetPayouts(player));
        }

        await SettleRoundWithRetryInternalAsync(players);

        _paid = true;
        ReleaseAllSeatsInternal();
        _dirty = true;
    }

    private async Task SettleRoundWithRetryInternalAsync(BjSettlement[] players)
    {
        for (var attempt = 1;; attempt++)
        {
            try
            {
                await ledger.SettleRoundAsync(GuildId, ChannelId, players);
                return;
            }
            catch (Exception ex) when (attempt < SETTLE_ATTEMPTS)
            {
                Log.Warning(ex, "Blackjack could not pay out the round in channel {ChannelId}, try {Attempt}",
                    ChannelId,
                    attempt);
                await Task.Delay(_settleRetryDelay * attempt, time);
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "Blackjack could not pay out the round in channel {ChannelId}. "
                    + "The stakes stay in the database and the bot refunds them later",
                    ChannelId);
                return;
            }
        }
    }

    // The players cannot act any more: every open hand stands and the dealer plays it out.
    private async Task StopInternalAsync()
    {
        if (_phase is not (BjTablePhase.Joining or BjTablePhase.Playing))
            return;

        // during the reveal the money is already paid, so only the animation stops
        if (_paid)
        {
            _dealerShown = int.MaxValue;
            _phase = BjTablePhase.Finished;
            return;
        }

        Stopped = true;

        if (Round is { } round)
        {
            round.Finish();
            await SettleInternalAsync();
            _phase = BjTablePhase.Finished;
            return;
        }

        foreach (var seat in _seats)
        {
            try
            {
                await ledger.RefundAsync(GuildId, ChannelId, seat.UserId, BjRefundReason.Stopped);
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "Blackjack could not refund the bet of {UserId} in channel {ChannelId}. The bot refunds it later",
                    seat.UserId,
                    ChannelId);
            }
        }

        ReleaseAllSeatsInternal();
        _phase = BjTablePhase.Finished;
    }

    private async Task FlushInternalAsync()
    {
        _dirty = false;
        if (_noEdits)
            return;

        var repost = IsRepostDue;
        _lastEdit = time.GetUtcNow();

        if (GetUsableChannelInternal() is not { } channel)
        {
            StopShowingInternal("the bot lacks a permission or the channel is gone");
            return;
        }

        var (embed, components) = renderer.Render(RenderState);
        try
        {
            if (_message is null)
                await PostTableInternalAsync(channel, embed, components);
            else if (!repost || !await TryRepostInternalAsync(channel, embed, components))
            {
                await _message.ModifyAsync(m =>
                {
                    m.Embed = embed;
                    m.Components = components;
                });
            }

            _editFailures = 0;
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage && _reposts < MAX_REPOSTS)
        {
            _reposts++;
            _message = null;
            _dirty = true;
            _lastEdit = default;
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            if (ex.DiscordCode == DiscordErrorCode.UnknownMessage)
                _message = null;

            StopShowingInternal(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Blackjack could not update the table in channel {ChannelId}", ChannelId);
            if (++_editFailures >= MAX_EDIT_FAILURES)
                StopShowingInternal(ex.Message);
            else
                _dirty = true;
        }
    }

    // A failed move keeps the old message, which this flush edits instead; the gate stays due, so a later flush tries again.
    private async Task<bool> TryRepostInternalAsync(SocketTextChannel channel, Embed embed, MessageComponent components)
    {
        try
        {
            await PostTableInternalAsync(channel, embed, components);
            return true;
        }
        catch (Exception ex) when (ex is not HttpException { HttpCode: HttpStatusCode.Forbidden })
        {
            Log.Warning(ex, "Blackjack could not move the table to the bottom of channel {ChannelId}", ChannelId);
            return false;
        }
    }

    // A new table message goes to the bottom of the chat; the old one is deleted after the send succeeds.
    private async Task PostTableInternalAsync(SocketTextChannel channel, Embed embed, MessageComponent components)
    {
        var old = _message;
        var sent = await channel.SendMessageAsync(embed: embed,
            components: components,
            allowedMentions: AllowedMentions.None);

        _message = sent;
        _repostGate.OnTablePosted(sent.Id);

        if (old is not null)
            _ = DeleteOldTableInternalAsync(old);
    }

    // Runs beside the loop, at most once per repost. An old message which stays loses its buttons, so it does not look live.
    private async Task DeleteOldTableInternalAsync(IUserMessage old)
    {
        try
        {
            await old.DeleteAsync();
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Blackjack could not delete the old table message in channel {ChannelId}", ChannelId);
            try
            {
                await old.ModifyAsync(static m => m.Components = new ComponentBuilder().Build());
            }
            catch (Exception editEx)
            {
                Log.Debug(editEx, "Blackjack could not remove the buttons of the old table message in channel {ChannelId}",
                    ChannelId);
            }
        }
    }

    private string? ResolveNameInternal(ulong userId)
    {
        var name = client.GetGuild(GuildId)?.GetUser(userId)?.DisplayName;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (name.Length > MAX_NAME_LENGTH)
            name = name[..(char.IsHighSurrogate(name[MAX_NAME_LENGTH - 1]) ? MAX_NAME_LENGTH - 1 : MAX_NAME_LENGTH)];

        return Format.Sanitize(name);
    }

    // The game does not depend on the output: a channel which cannot show the table only stops the edits.
    // The round still ends through its deadlines, and the settlement is the same as after a shown round.
    private void StopShowingInternal(string reason)
    {
        if (_noEdits)
            return;

        Log.Warning("Blackjack table in channel {ChannelId} cannot show its message and shows no more: {Reason}",
            ChannelId,
            reason);
        _noEdits = true;
    }

    // One try to show the stopped table, also when the table message is gone but the channel still works.
    private async Task ShowFinalInternalAsync()
    {
        if (GetUsableChannelInternal() is not { } channel)
            return;

        var (embed, components) = renderer.Render(RenderState);
        if (_message is { } message)
        {
            try
            {
                await message.ModifyAsync(m =>
                {
                    m.Embed = embed;
                    m.Components = components;
                });
                return;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Blackjack could not edit the stopped table in channel {ChannelId}", ChannelId);
            }
        }

        try
        {
            _message = await channel.SendMessageAsync(embed: embed,
                components: components,
                allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Blackjack could not post the stopped table in channel {ChannelId}", ChannelId);
        }
    }

    private SocketTextChannel? GetUsableChannelInternal()
    {
        var guild = client.GetGuild(GuildId);
        if (guild?.CurrentUser is not { } me || guild.GetChannel(ChannelId) is not SocketTextChannel channel)
            return null;

        var perms = me.GetPermissions(channel);
        var canSend = channel is IThreadChannel ? perms.SendMessagesInThreads : perms.SendMessages;
        return perms.ViewChannel && canSend && perms.EmbedLinks ? channel : null;
    }

    private async Task UpdateViaInteractionInternalAsync(SocketMessageComponent smc)
    {
        if (_noEdits)
        {
            await AckInternalAsync(smc);
            return;
        }

        var (embed, components) = renderer.Render(RenderState);
        try
        {
            await smc.UpdateAsync(m =>
            {
                m.Embed = embed;
                m.Components = components;
            });
            _dirty = false;
            _lastEdit = time.GetUtcNow();
            _editFailures = 0;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Blackjack could not answer a click in channel {ChannelId}", ChannelId);
            if (++_editFailures >= MAX_EDIT_FAILURES)
                StopShowingInternal(ex.Message);
            else
                _dirty = true;
        }
    }

    // The deferred click already shows the old table; this one edit of the same message does not use the global rate limit.
    private async Task ShowViaOriginalResponseInternalAsync(SocketMessageComponent smc)
    {
        if (_noEdits)
            return;

        var (embed, components) = renderer.Render(RenderState);
        try
        {
            await smc.ModifyOriginalResponseAsync(m =>
            {
                m.Embed = embed;
                m.Components = components;
            });
            _dirty = false;
            _lastEdit = time.GetUtcNow();
            _editFailures = 0;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Blackjack could not show the table after a click in channel {ChannelId}", ChannelId);
            _dirty = true;
        }
    }

    private static async Task RefuseAfterDeferInternalAsync(SocketMessageComponent smc, string text)
    {
        try
        {
            await smc.FollowupAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Blackjack could not answer a refused click");
        }
    }

    private static async Task RefuseInternalAsync(SocketMessageComponent smc, string text)
    {
        try
        {
            await smc.RespondAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Blackjack could not answer a refused click");
        }
    }

    private static async Task AckInternalAsync(SocketMessageComponent smc)
    {
        try
        {
            await smc.DeferAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Blackjack could not acknowledge a click");
        }
    }

    private void PublishSeatsInternal()
    {
        var ids = new ulong[_seats.Count];
        for (var i = 0; i < ids.Length; i++)
            ids[i] = _seats[i].UserId;

        _seatIds = ids;
    }

    private void ReleaseAllSeatsInternal()
    {
        var ids = _seatIds;
        _seatIds = [];
        foreach (var id in ids)
            service.ReleaseSeat(id);
    }

    // default: a wake event from the timer
    private readonly record struct TableEvent(
        ulong UserId,
        long Bet,
        SocketMessageComponent? Click,
        TaskCompletionSource<BjJoinResult>? Reply)
    {
        public bool IsWake
            => Click is null && Reply is null;
    }

    // default: the click changed nothing and gets no answer of its own
    private readonly record struct ClickOutcome(bool IsChanged, string? Refusal)
    {
        public static ClickOutcome Changed
            => new(true, null);

        public static ClickOutcome Refuse(string text)
            => new(false, text);
    }
}
