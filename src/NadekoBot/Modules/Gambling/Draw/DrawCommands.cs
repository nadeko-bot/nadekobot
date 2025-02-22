#nullable disable
using Nadeko.Econ;
using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public partial class DrawCommands : GamblingModule<IGamblingService>
    {
        private static readonly ConcurrentDictionary<IGuild, Deck> _allDecks = new();
        private readonly IImageCache _images;

        public DrawCommands(IImageCache images, GamblingConfigService gcs)
            : base(gcs)
            => _images = images;

        private async Task InternalDraw(int count, ulong? guildId = null)
        {
            if (count is < 1 or > 10)
                throw new ArgumentOutOfRangeException(nameof(count));

            var cards = guildId is null ? new() : _allDecks.GetOrAdd(ctx.Guild, _ => new());
            var images = new List<Image<Rgba32>>();
            var cardObjects = new List<Deck.Card>();
            for (var i = 0; i < count; i++)
            {
                if (cards.CardPool.Count == 0 && i != 0)
                {
                    try
                    {
                        await Response().Error(strs.no_more_cards).SendAsync();
                    }
                    catch
                    {
                        // ignored
                    }

                    break;
                }

                var currentCard = cards.Draw();
                cardObjects.Add(currentCard);
                var image = await GetCardImageAsync(currentCard);
                images.Add(image);
            }

            var imgName = "cards.jpg";
            using var img = images.Merge();
            foreach (var i in images)
                i.Dispose();

            var eb = CreateEmbed()
                .WithOkColor();

            var toSend = string.Empty;
            if (cardObjects.Count == 5)
                eb.AddField(GetText(strs.hand_value), Deck.GetHandValue(cardObjects), true);

            if (guildId is not null)
                toSend += GetText(strs.cards_left(Format.Bold(cards.CardPool.Count.ToString())));

            eb.WithDescription(toSend)
              .WithAuthor(ctx.User)
              .WithImageUrl($"attachment://{imgName}");

            if (count > 1)
                eb.AddField(GetText(strs.cards), count.ToString(), true);

            await using var imageStream = await img.ToStreamAsync();
            await ctx.Channel.SendFileAsync(imageStream,
                imgName,
                embed: eb.Build());
        }

        private async Task<Image<Rgba32>> GetCardImageAsync(RegularCard currentCard)
        {
            var cardName = currentCard.GetName().ToLowerInvariant().Replace(' ', '_');
            var cardBytes = await File.ReadAllBytesAsync($"data/images/cards/{cardName}.jpg");
            return Image.Load<Rgba32>(cardBytes);
        }

        private async Task<Image<Rgba32>> GetCardImageAsync(Deck.Card currentCard)
        {
            var cardName = currentCard.ToString().ToLowerInvariant().Replace(' ', '_');
            var cardBytes = await File.ReadAllBytesAsync($"data/images/cards/{cardName}.jpg");
            return Image.Load<Rgba32>(cardBytes);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Draw(int num = 1)
        {
            if (num < 1)
                return;

            if (num > 10)
                num = 10;

            await InternalDraw(num, ctx.Guild.Id);
        }

        [Cmd]
        public async Task DrawNew(int num = 1)
        {
            if (num < 1)
                return;

            if (num > 10)
                num = 10;

            await InternalDraw(num);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task DeckShuffle()
        {
            //var channel = (ITextChannel)ctx.Channel;

            _allDecks.AddOrUpdate(ctx.Guild,
                _ => new(),
                (_, c) =>
                {
                    c.Restart();
                    return c;
                });

            await Response().Confirm(strs.deck_reshuffled).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public Task BetDraw(
            [OverrideTypeReader(typeof(BalanceTypeReader))]
            long amount,
            InputValueGuess val,
            InputColorGuess? col = null)
            => BetDrawInternal(amount, val, col);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public Task BetDraw(
            [OverrideTypeReader(typeof(BalanceTypeReader))]
            long amount,
            InputColorGuess col,
            InputValueGuess? val = null)
            => BetDrawInternal(amount, val, col);

        public async Task BetDrawInternal(long amount, InputValueGuess? val, InputColorGuess? col)
        {
            if (!await CheckBetMandatory(amount))
            {
                return;
            }

            var res = await _service.BetDrawAsync(ctx.User.Id,
                amount,
                (byte?)val,
                (byte?)col);

            if (!res.TryPickT0(out var result, out _))
            {
                await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
                return;
            }

            var eb = CreateEmbed()
                     .WithOkColor()
                     .WithAuthor(ctx.User)
                     .WithDescription(result.Card.GetEmoji())
                     .AddField(GetText(strs.guess), GetGuessInfo(val, col), true)
                     .AddField(GetText(strs.card), GetCardInfo(result.Card), false)
                     .AddField(GetText(strs.bet), N(amount), true)
                     .AddField(GetText(strs.won), N((long)result.Won), true)
                     .WithImageUrl("attachment://card.png");

            using var img = await GetCardImageAsync(result.Card);
            await using var imgStream = await img.ToStreamAsync();
            await ctx.Channel.SendFileAsync(imgStream, "card.png", embed: eb.Build());
        }

        private string GetGuessInfo(InputValueGuess? valG, InputColorGuess? colG)
        {
            var val = valG switch
            {
                InputValueGuess.H => "Hi ⬆️",
                InputValueGuess.L => "Lo ⬇️",
                _ => "❓"
            };

            var col = colG switch
            {
                InputColorGuess.Red => "R 🔴",
                InputColorGuess.Black => "B ⚫",
                _ => "❓"
            };

            return $"{val} / {col}";
        }

        private string GetCardInfo(RegularCard card)
        {
            var val = (int)card.Value switch
            {
                < 7 => "Lo ⬇️",
                > 7 => "Hi ⬆️",
                _ => "7 💀"
            };

            var col = card.Value == RegularValue.Seven
                ? "7 💀"
                : card.Suit switch
                {
                    RegularSuit.Diamonds or RegularSuit.Hearts => "R 🔴",
                    _ => "B ⚫"
                };

            return $"{val} / {col}";
        }

        public enum InputValueGuess
        {
            High = 0,
            H = 0,
            Hi = 0,
            Low = 1,
            L = 1,
            Lo = 1,
        }

        public enum InputColorGuess
        {
            R = 0,
            Red = 0,
            B = 1,
            Bl = 1,
            Black = 1,
        }
    }
}