#nullable disable
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
    public partial class FlipCoinCommands : GamblingModule<IGamblingService>
    {
        public enum BetFlipGuess : byte
        {
            H = 0,
            Head = 0,
            Heads = 0,
            T = 1,
            Tail = 1,
            Tails = 1
        }

        private static readonly NadekoRandom _rng = new();
        private readonly IImageCache _images;
        private readonly ICurrencyService _cs;
        private readonly ImagesConfig _ic;

        public FlipCoinCommands(
            IImageCache images,
            ImagesConfig ic,
            ICurrencyService cs,
            GamblingConfigService gss)
            : base(gss)
        {
            _ic = ic;
            _images = images;
            _cs = cs;
        }

        [Cmd]
        public async Task Flip(int count = 1)
        {
            if (count is > 10 or < 1)
            {
                await Response().Error(strs.flip_invalid(10)).SendAsync();
                return;
            }

            var headCount = 0;
            var tailCount = 0;
            var imgs = new Image<Rgba32>[count];
            var headsArr = await _images.GetHeadsImageAsync();
            var tailsArr = await _images.GetTailsImageAsync();

            var result = await _service.FlipAsync(count);

            for (var i = 0; i < result.Length; i++)
            {
                if (result[i].Side == 0)
                {
                    imgs[i] = Image.Load<Rgba32>(headsArr);
                    headCount++;
                }
                else
                {
                    imgs[i] = Image.Load<Rgba32>(tailsArr);
                    tailCount++;
                }
            }

            using var img = imgs.Merge(out var format);
            await using var stream = await img.ToStreamAsync(format);
            foreach (var i in imgs)
                i.Dispose();

            var imgName = $"coins.{format.FileExtensions.First()}";

            var msg = count != 1
                ? Format.Bold(GetText(strs.flip_results(count, headCount, tailCount)))
                : GetText(strs.flipped(headCount > 0
                    ? Format.Bold(GetText(strs.heads))
                    : Format.Bold(GetText(strs.tails))));

            var eb = CreateEmbed()
                     .WithOkColor()
                     .WithAuthor(ctx.User)
                     .WithDescription(msg)
                     .WithImageUrl($"attachment://{imgName}");

            await ctx.Channel.SendFileAsync(stream,
                imgName,
                embed: eb.Build());
        }

        [Cmd]
        public async Task Betflip([OverrideTypeReader(typeof(BalanceTypeReader))] long amount, BetFlipGuess guess)
        {
            if (!await CheckBetMandatory(amount) || amount == 1)
                return;

            var res = await _service.BetFlipAsync(ctx.User.Id, amount, (byte)guess);
            if (!res.TryPickT0(out var result, out _))
            {
                await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
                return;
            }

            Uri imageToSend;
            var coins = _ic.Data.Coins;
            if (result.Side == 0)
            {
                imageToSend = coins.Heads[_rng.Next(0, coins.Heads.Length)];
            }
            else
            {
                imageToSend = coins.Tails[_rng.Next(0, coins.Tails.Length)];
            }

            string str;
            var won = (long)result.Won;
            if (won > 0)
            {
                str = Format.Bold(GetText(strs.betflip_guess));
            }
            else
            {
                str = Format.Bold(GetText(strs.better_luck));
            }

            await Response()
                  .Embed(CreateEmbed()
                         .WithAuthor(ctx.User)
                         .WithDescription(str)
                         .AddField(GetText(strs.bet), N(amount), true)
                         .AddField(GetText(strs.won), N((long)result.Won), true)
                         .WithOkColor()
                         .WithImageUrl(imageToSend.ToString()))
                  .SendAsync();
        }
    }
}