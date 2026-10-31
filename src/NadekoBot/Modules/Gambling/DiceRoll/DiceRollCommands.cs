#nullable disable
using SixLabors.ImageSharp;
using System.Text;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public partial class DiceRollCommands : NadekoModule
    {
        private static readonly char[] _fateRolls = ['-', ' ', '+'];
        private readonly IImageCache _images;

        public DiceRollCommands(IImageCache images)
            => _images = images;

        [Cmd]
        public async Task Roll()
        {
            var rng = new NadekoRandom();
            var gen = rng.Next(1, 101);

            var num1 = gen / 10;
            var num2 = gen % 10;
            
            using var img1 = await GetDiceAsync(num1);
            using var img2 = await GetDiceAsync(num2);
            using var img = new[] { img1, img2 }.Merge(out var format);
            await using var ms = await img.ToStreamAsync(format);

            var fileName = $"dice.{format.FileExtensions.First()}";

            var eb = CreateEmbed()
                .WithOkColor()
                .WithAuthor(ctx.User)
                .AddField(GetText(strs.roll2), gen)
                .WithImageUrl($"attachment://{fileName}");

            await ctx.Channel.SendFileAsync(ms,
                fileName,
                embed: eb.Build());
        }

        [Cmd]
        [Priority(1)]
        public async Task Roll(int num)
            => await InternalRoll(num, true);


        [Cmd]
        [Priority(1)]
        public async Task Rolluo(int num = 1)
            => await InternalRoll(num, false);

        [Cmd]
        [Priority(0)]
        public async Task Roll(string arg)
            => await InternallDndRoll(arg, true);

        [Cmd]
        [Priority(0)]
        public async Task Rolluo(string arg)
            => await InternallDndRoll(arg, false);

        private async Task InternalRoll(int num, bool ordered)
        {
            if (num is < 1 or > 30)
            {
                await Response().Error(strs.dice_invalid_number(1, 30)).SendAsync();
                return;
            }

            var rng = new NadekoRandom();

            var dice = new List<Image<Rgba32>>(num);
            var values = new List<int>(num);
            for (var i = 0; i < num; i++)
            {
                var randomNumber = rng.Next(1, 7);
                var toInsert = dice.Count;
                if (ordered)
                {
                    if (randomNumber == 6 || dice.Count == 0)
                        toInsert = 0;
                    else if (randomNumber != 1)
                    {
                        for (var j = 0; j < dice.Count; j++)
                        {
                            if (values[j] < randomNumber)
                            {
                                toInsert = j;
                                break;
                            }
                        }
                    }
                }
                else
                    toInsert = dice.Count;

                dice.Insert(toInsert, await GetDiceAsync(randomNumber));
                values.Insert(toInsert, randomNumber);
            }

            using var bitmap = dice.Merge(out var format);
            await using var ms = bitmap.ToStream(format);
            foreach (var d in dice)
                d.Dispose();

            var imageName = $"dice.{format.FileExtensions.First()}";
            var eb = CreateEmbed()
                .WithOkColor()
                .WithAuthor(ctx.User)
                .AddField(GetText(strs.rolls), values.Select(x => Format.Code(x.ToString())).Join(' '), true)
                .AddField(GetText(strs.total), values.Sum(), true)
                .WithDescription(GetText(strs.dice_rolled_num(Format.Bold(values.Count.ToString()))))
                .WithImageUrl($"attachment://{imageName}");

            await ctx.Channel.SendFileAsync(ms,
                imageName,
                embed: eb.Build());
        }

        private async Task InternallDndRoll(string arg, bool ordered)
        {
            if (!DiceParser.TryParseDnd(arg, out var roll))
            {
                await Response().Error(strs.dice_invalid_dnd).SendAsync();
                return;
            }

            var maxDice = roll.IsFate ? DiceParser.MAX_FATE_DICE : DiceParser.MAX_DICE;
            if (roll.Count < 1 || roll.Count > maxDice)
            {
                await Response().Error(strs.dice_invalid_number(1, maxDice)).SendAsync();
                return;
            }

            if (roll.Sides > DiceParser.MAX_SIDES)
            {
                await Response().Error(strs.dice_invalid_sides(DiceParser.MAX_SIDES)).SendAsync();
                return;
            }

            var rng = new NadekoRandom();
            if (roll.IsFate)
            {
                var rolls = new StringBuilder(roll.Count * 6);
                for (var i = 0; i < roll.Count; i++)
                {
                    if (i > 0)
                        rolls.Append(' ');

                    rolls.Append("`[").Append(_fateRolls[rng.Next(0, _fateRolls.Length)]).Append("]`");
                }

                var fateEmbed = CreateEmbed()
                    .WithOkColor()
                    .WithAuthor(ctx.User)
                    .WithDescription(GetText(strs.dice_rolled_num(Format.Bold(roll.Count.ToString()))))
                    .AddField(Format.Bold(GetText(strs.rolls)), rolls.ToString());

                await Response().Embed(fateEmbed).SendAsync();
                return;
            }

            var arr = new int[roll.Count];
            long sum = 0;
            for (var i = 0; i < arr.Length; i++)
            {
                arr[i] = rng.Next(1, roll.Sides + 1);
                sum += arr[i];
            }

            if (ordered)
                Array.Sort(arr);

            var rollsText = new StringBuilder(arr.Length * 9);
            for (var i = 0; i < arr.Length; i++)
            {
                if (i > 0)
                    rollsText.Append(' ');

                rollsText.Append('`').Append(arr[i]).Append('`');
            }

            var embed = CreateEmbed()
                .WithOkColor()
                .WithAuthor(ctx.User)
                .WithDescription(GetText(strs.dice_rolled_num($"{Format.Bold(roll.Count.ToString())} {Format.Code($"d{roll.Sides}")}")))
                .AddField(Format.Bold(GetText(strs.rolls)), rollsText.ToString())
                .AddField(Format.Bold(GetText(strs.total)),
                    $"{sum} + {roll.Add} - {roll.Sub} = {sum + roll.Add - roll.Sub}");

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        public async Task NRoll([Leftover] string range)
        {
            if (!DiceParser.TryParseRange(range, out var min, out var max))
            {
                await Response().Error(strs.dice_invalid_range).SendAsync();
                return;
            }

            if (min > max)
            {
                await Response().Error(strs.second_larger_than_first).SendAsync();
                return;
            }

            var rolled = new NadekoRandom().NextLong(min, max + 1);
            await Response().Confirm(strs.dice_rolled(Format.Bold(rolled.ToString()))).SendAsync();
        }

        private async Task<Image<Rgba32>> GetDiceAsync(int num)
        {
            if (num is < 0 or > 10)
                throw new ArgumentOutOfRangeException(nameof(num));

            if (num == 10)
            {
                using var imgOne = Image.Load<Rgba32>(await _images.GetDiceAsync(1));
                using var imgZero = Image.Load<Rgba32>(await _images.GetDiceAsync(0));
                return new[] { imgOne, imgZero }.Merge();
            }

            return Image.Load<Rgba32>(await _images.GetDiceAsync(num));
        }
    }
}