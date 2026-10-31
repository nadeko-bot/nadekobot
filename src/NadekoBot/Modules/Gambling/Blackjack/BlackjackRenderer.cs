using System.Globalization;
using System.Text;
using NadekoBot.Modules.Gambling.Services;

namespace NadekoBot.Modules.Gambling.Blackjack;

// Everything the table message shows. DealerShown limits the dealer's cards while the bot reveals them one by one.
public readonly record struct BjRenderState(
    ulong GuildId,
    string CommandCode,
    BjTablePhase Phase,
    bool Stopped,
    BlackjackRound? Round,
    List<BjSeat> Seats,
    List<string?> Names,
    DateTimeOffset Deadline,
    int Version,
    int DealerShown);

public sealed class BlackjackRenderer(
    IBotStrings strings,
    ILocalization localization,
    GamblingConfigService gamblingConfig,
    IMessageSenderService sender) : INService
{
    // two regional indicator letters next to each other form a flag, so cards are always separated by a space
    private static readonly string[] _ranks = ["🇦", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣", "🔟", "🇯", "🇶", "🇰"];
    private static readonly string[] _suits = ["♠️", "♥️", "♦️", "♣️"];

    private const string SEPARATOR = " · ";
    private const string ICON_TURN = "▶";
    private const string ICON_STOOD = "⏹";
    private const string ICON_BUST = "💥";
    private const string ICON_NATURAL = "🃏";
    private const string ICON_BLACKJACK_PAID = "💰";
    private const string ICON_WIN = "✅";
    private const string ICON_LOSS = "❌";
    private const string ICON_PUSH = "➖";
    private const string ICON_DEALER_PLAYING = "❔";
    private const string ICON_DEALER_DONE = "🏁";
    private const string DOUBLED = " ✖2";
    private const string PAYOUT_ARROW = " → ";
    private const int TEXT_CAPACITY = 256;
    public const string COMMAND_SYNTAX = "bj <amount>";

    // Render never awaits, so one builder per thread is safe to reuse.
    [ThreadStatic]
    private static StringBuilder? _builder;

    public string GetText(ulong guildId, LocStr str)
        => strings.GetText(str.Key, guildId, str.Params);

    public string GetNotEnoughText(ulong guildId)
        => GetText(guildId, strs.not_enough(gamblingConfig.Data.Currency.Sign));

    public (Embed Embed, MessageComponent Components) Render(in BjRenderState state)
    {
        var view = new View(this, state.GuildId, localization.GetCultureInfo(state.GuildId), gamblingConfig.Data.Currency.Sign);
        var sb = _builder ??= new StringBuilder(TEXT_CAPACITY);
        sb.Clear();

        var eb = sender.CreateEmbed(state.GuildId);
        if (state.Stopped)
            eb.WithErrorColor();
        else
            eb.WithOkColor();

        eb.WithTitle(view.T(strs.bj_title));

        if (state.Round is { } round)
            AppendRoundInternal(eb, sb, state, round, view);
        else
            eb.WithDescription(JoiningDescriptionInternal(sb, state, view));

        return (eb.Build(), BuildComponentsInternal(state, view));
    }

    private static string JoiningDescriptionInternal(StringBuilder sb, in BjRenderState state, View view)
    {
        if (state.Stopped)
            return view.T(strs.bj_stopped_joining);

        sb.Append(view.T(strs.bj_join_line(state.CommandCode)));

        for (var i = 0; i < state.Seats.Count; i++)
        {
            var seat = state.Seats[i];
            sb.Append('\n').Append(i + 1).Append(". ")
              .Append(Mention(seat.UserId))
              .Append(SEPARATOR)
              .Append(view.N(seat.Bet));
        }

        sb.Append("\n\n").Append(view.T(strs.bj_starts(Timestamp(state.Deadline))));
        return sb.ToString();
    }

    private static void AppendRoundInternal(
        EmbedBuilder eb,
        StringBuilder sb,
        in BjRenderState state,
        BlackjackRound round,
        View view)
    {
        var dealer = round.Dealer;
        var shown = Math.Min(state.DealerShown, dealer.Count);
        var revealed = round.Phase == BjRoundPhase.Finished && shown >= dealer.Count;

        if (DescriptionInternal(state, round, revealed, view) is { } description)
            eb.WithDescription(description);

        var dealerCards = dealer.Cards[..shown];
        var dealerValue = BlackjackHand.Evaluate(dealerCards);
        var dealerNatural = shown == 2 && dealerValue.Total == BlackjackHand.BLACKJACK;

        var dealerIcon = !revealed
            ? ICON_DEALER_PLAYING
            : dealerNatural
                ? ICON_NATURAL
                : dealerValue.IsBust
                    ? ICON_BUST
                    : ICON_DEALER_DONE;

        sb.Clear()
          .Append(dealerIcon).Append(' ')
          .Append(view.T(strs.bj_dealer))
          .Append(SEPARATOR);
        AppendTotalInternal(sb, dealerValue, dealerNatural, view);
        var dealerName = sb.ToString();

        sb.Clear();
        AppendCardLinesInternal(sb, dealerCards);
        eb.AddField(dealerName, sb.ToString(), inline: false);

        for (var p = 0; p < round.Players.Length; p++)
        {
            var player = round.Players[p];
            var name = p < state.Names.Count ? state.Names[p] : null;

            for (var h = 0; h < player.Hands.Count; h++)
            {
                var hand = player.Hands[h];

                sb.Clear();
                if (HandIconInternal(round, p, h, hand, revealed) is { } icon)
                    sb.Append(icon).Append(' ');

                sb.Append(name ?? view.T(strs.bj_player(p + 1)));
                if (player.Hands.Count > 1)
                    sb.Append(" (").Append(h + 1).Append(')');

                sb.Append(SEPARATOR);
                AppendTotalInternal(sb, hand.Value, hand.IsNatural, view);
                if (hand.IsDoubled)
                    sb.Append(DOUBLED);

                var fieldName = sb.ToString();

                sb.Clear();
                AppendCardLinesInternal(sb, hand.Cards);
                sb.Append('\n');

                // a member who is not in the cache has no name, so the mention shows who plays the hand
                if (name is null)
                    sb.Append(Mention(player.UserId)).Append(' ');

                // the amount after the arrow is everything the hand paid back; a lost hand shows only its bet
                sb.Append(view.N(hand.Bet));
                if (revealed && hand.Payout > 0)
                    sb.Append(PAYOUT_ARROW).Append(view.N(hand.Payout));

                eb.AddField(fieldName, sb.ToString(), inline: true);
            }
        }
    }

    private static string? DescriptionInternal(in BjRenderState state, BlackjackRound round, bool revealed, View view)
    {
        if (state.Stopped)
            return view.T(strs.bj_stopped);

        if (revealed)
            return view.T(strs.bj_new_table(state.CommandCode));

        if (round.CurrentPlayer is not { } turn)
            return view.T(strs.bj_dealer_draws);

        return turn.Hands.Count > 1
            ? view.T(strs.bj_turn_hand(Mention(turn.UserId), round.HandIndex + 1, Timestamp(state.Deadline)))
            : view.T(strs.bj_turn(Mention(turn.UserId), Timestamp(state.Deadline)));
    }

    private static string? HandIconInternal(BlackjackRound round, int playerIndex, int handIndex, BlackjackHand hand, bool revealed)
    {
        if (revealed)
        {
            return hand.Result switch
            {
                BjHandResult.Blackjack => ICON_BLACKJACK_PAID,
                BjHandResult.Win => ICON_WIN,
                BjHandResult.Push => ICON_PUSH,
                BjHandResult.Bust => ICON_BUST,
                _ => ICON_LOSS
            };
        }

        if (round.Phase == BjRoundPhase.Players && round.PlayerIndex == playerIndex && round.HandIndex == handIndex)
            return ICON_TURN;

        if (hand.IsBust)
            return ICON_BUST;

        if (hand.IsNatural)
            return ICON_NATURAL;

        return hand.IsFinished ? ICON_STOOD : null;
    }

    private static MessageComponent BuildComponentsInternal(in BjRenderState state, View view)
    {
        var cb = new ComponentBuilder();
        var version = state.Version;

        if (state.Phase == BjTablePhase.Joining)
        {
            cb.WithButton(view.T(strs.bj_btn_start), BlackjackTable.ButtonId(BlackjackTable.ACT_START, version), ButtonStyle.Success);
            return cb.Build();
        }

        if (state.Phase != BjTablePhase.Playing || state.Round is not { Phase: BjRoundPhase.Players } round)
            return cb.Build();

        cb.WithButton(view.T(strs.bj_btn_hit), BlackjackTable.ButtonId(BlackjackTable.ACT_HIT, version), ButtonStyle.Primary)
          .WithButton(view.T(strs.bj_btn_stand), BlackjackTable.ButtonId(BlackjackTable.ACT_STAND, version), ButtonStyle.Secondary)
          .WithButton(view.T(strs.bj_btn_double), BlackjackTable.ButtonId(BlackjackTable.ACT_DOUBLE, version), ButtonStyle.Success, disabled: !round.CanDouble)
          .WithButton(view.T(strs.bj_btn_split), BlackjackTable.ButtonId(BlackjackTable.ACT_SPLIT, version), ButtonStyle.Success, disabled: !round.CanSplit);

        return cb.Build();
    }

    // the rank line, then the suit line, so each suit sits under its rank
    private static void AppendCardLinesInternal(StringBuilder sb, ReadOnlySpan<BjCard> cards)
    {
        for (var i = 0; i < cards.Length; i++)
        {
            if (i > 0)
                sb.Append(' ');

            sb.Append(_ranks[cards[i].Rank - 1]);
        }

        sb.Append('\n');
        for (var i = 0; i < cards.Length; i++)
        {
            if (i > 0)
                sb.Append(' ');

            sb.Append(_suits[cards[i].Suit]);
        }
    }

    private static void AppendTotalInternal(StringBuilder sb, BjHandValue value, bool natural, View view)
    {
        if (natural)
            sb.Append(view.T(strs.bj_blackjack));
        else if (value.IsSoft)
            sb.Append(view.T(strs.bj_soft(value.Total)));
        else
            sb.Append(value.Total);
    }

    private static string Mention(ulong userId)
        => MentionUtils.MentionUser(userId);

    private static string Timestamp(DateTimeOffset at)
        => TimestampTag.FormatFromDateTimeOffset(at, TimestampTagStyles.Relative);

    private readonly record struct View(BlackjackRenderer Renderer, ulong GuildId, CultureInfo Culture, string Sign)
    {
        public string T(LocStr str)
            => Renderer.GetText(GuildId, str);

        public string N(long amount)
            => CurrencyHelper.N(amount, Culture, Sign);
    }
}
