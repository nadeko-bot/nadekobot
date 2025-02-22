#nullable disable warnings
using NadekoBot.Db.Models;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Services;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using NadekoBot.Common.TypeReaders;
using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;

namespace NadekoBot.Modules.Gambling;

public enum GamblingError
{
    InsufficientFunds,
}

public partial class Gambling
{
    [Group]
    public partial class SlotCommands : GamblingModule<IGamblingService>
    {
        private readonly IImageCache _images;
        private readonly FontProvider _fonts;
        private readonly DbService _db;
        private object _slotStatsLock = new();

        public SlotCommands(
            IImageCache images,
            FontProvider fonts,
            DbService db,
            GamblingConfigService gamb)
            : base(gamb)
        {
            _images = images;
            _fonts = fonts;
            _db = db;
        }

        public Task Test()
            => Task.CompletedTask;

        [Cmd]
        public async Task Slot([OverrideTypeReader(typeof(BalanceTypeReader))] long amount)
        {
            if (!await CheckBetMandatory(amount))
                return;

            // var slotInteraction = CreateSlotInteractionIntenal(amount);

            await ctx.Channel.TriggerTypingAsync();

            if (await InternalSlotAsync(amount) is not SlotResult result)
            {
                await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
                return;
            }

            var text = GetSlotMessageTextInternal(result);

            using var image = await GenerateSlotImageAsync(amount, result);
            await using var imgStream = await image.ToStreamAsync();


            var eb = CreateEmbed()
                     .WithAuthor(ctx.User)
                     .WithDescription(Format.Bold(text))
                     .WithImageUrl($"attachment://result.png")
                     .WithOkColor();

            var bb = new ButtonBuilder(emote: Emoji.Parse("🔁"), customId: "slot:again", label: "Pull Again");
            var inter = _inter.Create(ctx.User.Id,
                bb,
                smc =>
                {
                    smc.DeferAsync();
                    return Slot(amount);
                });

            var msg = await ctx.Channel.SendFileAsync(imgStream,
                "result.png",
                embed: eb.Build(),
                components: inter.CreateComponent()
            );
            await inter.RunAsync(msg);
        }

        // private SlotInteraction CreateSlotInteractionIntenal(long amount)
        // {
        //     return new SlotInteraction((DiscordSocketClient)ctx.Client,
        //         ctx.User.Id,
        //         async (smc) =>
        //         {
        //             try
        //             {
        //                 if (await InternalSlotAsync(amount) is not SlotResult result)
        //                 {
        //                     await smc.RespondErrorAsync(_eb, GetText(strs.not_enough(CurrencySign)), true);
        //                     return;
        //                 }
        //
        //                 var msg = GetSlotMessageInternal(result);
        //
        //                 using var image = await GenerateSlotImageAsync(amount, result);
        //                 await using var imgStream = await image.ToStreamAsync();
        //
        //                 var guid = Guid.NewGuid();
        //                 var imgName = $"result_{guid}.png";
        //                 
        //                 var slotInteraction = CreateSlotInteractionIntenal(amount).GetInteraction();
        //                 
        //                 await smc.Message.ModifyAsync(m =>
        //                 {
        //                     m.Content = msg;
        //                     m.Attachments = new[]
        //                     {
        //                         new FileAttachment(imgStream, imgName)
        //                     };
        //                     m.Components = slotInteraction.CreateComponent();
        //                 });
        //                 
        //                 _ = slotInteraction.RunAsync(smc.Message);
        //             }
        //             catch (Exception ex)
        //             {
        //                 Log.Error(ex, "Error pulling slot again");
        //             }
        //             // finally
        //             // {
        //             //     await Task.Delay(1000);
        //             //     _runningUsers.TryRemove(ctx.User.Id);
        //             // }
        //         });
        // }

        private string GetSlotMessageTextInternal(SlotResult result)
        {
            var multi = result.Multiplier.ToString("0.##");
            var msg = result.WinType switch
            {
                SlotWinType.SingleJoker => GetText(strs.slot_single(CurrencySign, multi)),
                SlotWinType.DoubleJoker => GetText(strs.slot_two(CurrencySign, multi)),
                SlotWinType.TrippleNormal => GetText(strs.slot_three(multi)),
                SlotWinType.TrippleJoker => GetText(strs.slot_jackpot(multi)),
                _ => GetText(strs.better_luck),
            };
            return msg;
        }

        private async Task<SlotResult?> InternalSlotAsync(long amount)
        {
            var maybeResult = await _service.SlotAsync(ctx.User.Id, amount);

            if (!maybeResult.TryPickT0(out var result, out var error))
            {
                return null;
            }

            return result;
        }

        private async Task<Image<Rgba32>> GenerateSlotImageAsync(long amount, SlotResult result)
        {
            long ownedAmount;
            await using (var uow = _db.GetDbContext())
            {
                ownedAmount = uow.Set<DiscordUser>()
                                 .FirstOrDefault(x => x.UserId == ctx.User.Id)
                                 ?.CurrencyAmount
                              ?? 0;
            }

            var slotBg = await _images.GetSlotBgAsync();
            var bgImage = Image.Load<Rgba32>(slotBg);
            var numbers = new int[3];
            result.Rolls.CopyTo(numbers, 0);

            Color fontColor = Config.Slots.CurrencyFontColor;

            bgImage.Mutate<Rgba32>(x => x.DrawText(new RichTextOptions(_fonts.DottyFont.CreateFont(65))
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 140,
                    Origin = new(298, 100)
                },
                ((long)result.Won).ToString(),
                fontColor));

            var bottomFont = _fonts.DottyFont.CreateFont(50);

            bgImage.Mutate(x => x.DrawText(new RichTextOptions(bottomFont)
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 135,
                    Origin = new(196, 480)
                },
                amount.ToString(),
                fontColor));

            bgImage.Mutate(x => x.DrawText(new(bottomFont)
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Origin = new(393, 480)
                },
                ownedAmount.ToString(),
                fontColor));
            //sw.PrintLap("drew red text");

            for (var i = 0; i < 3; i++)
            {
                using var img = Image.Load(await _images.GetSlotEmojiAsync(numbers[i]));
                bgImage.Mutate(x => x.DrawImage(img, new Point(148 + (105 * i), 217), 1f));
            }

            return bgImage;
        }
    }
}