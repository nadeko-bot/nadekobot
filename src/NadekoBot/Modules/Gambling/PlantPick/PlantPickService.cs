#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Games.Quests;
using System.Globalization;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;

namespace NadekoBot.Modules.Gambling.Services;

public class PlantPickService(
    DbService db,
    IBotStrings strings,
    IImageCache images,
    FontProvider fonts,
    ICurrencyService cs,
    CommandHandler cmdHandler,
    DiscordSocketClient client,
    GamblingConfigService gss,
    GamblingService gs,
    QuestService quests) : INService, IExecNoCommand, IReadyExecutor
{
    //channelId/last generation
    public ConcurrentDictionary<ulong, long> LastGenerations { get; } = new();
    private ConcurrentHashSet<ulong> _generationChannels = [];

#nullable enable
    public ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
#nullable disable
        => PotentialFlowerGeneration(msg);

    private string GetText(ulong gid, LocStr str)
        => strings.GetText(str, gid);

    public async Task<bool> ToggleCurrencyGeneration(ulong gid, ulong cid)
    {
        bool enabled;
        await using var uow = db.GetDbContext();

        if (_generationChannels.Add(cid))
        {
            await uow.GetTable<GCChannelId>()
                .InsertOrUpdateAsync(() => new()
                    {
                        ChannelId = cid,
                        GuildId = gid
                    },
                    (x) => new()
                    {
                        ChannelId = cid,
                        GuildId = gid
                    },
                    () => new()
                    {
                        ChannelId = cid,
                        GuildId = gid
                    });

            _generationChannels.Add(cid);
            enabled = true;
        }
        else
        {
            await uow.GetTable<GCChannelId>()
                .Where(x => x.ChannelId == cid && x.GuildId == gid)
                .DeleteAsync();

            _generationChannels.TryRemove(cid);
            enabled = false;
        }

        return enabled;
    }

    public async Task<IReadOnlyCollection<GCChannelId>> GetAllGeneratingChannels()
    {
        await using var uow = db.GetDbContext();
        return await uow.GetTable<GCChannelId>()
            .ToListAsyncLinqToDB();
    }

    /// <summary>
    ///     Get a random currency image stream, with an optional password sticked onto it.
    /// </summary>
    /// <param name="pass">Optional password to add to top left corner.</param>
    /// <returns>Stream of the currency image</returns>
    public async Task<(Stream, string)> GetRandomCurrencyImageAsync(string pass)
    {
        var curImg = await images.GetCurrencyImageAsync();

        if (curImg is null)
            return (new MemoryStream(), null);

        if (string.IsNullOrWhiteSpace(pass))
        {
            // determine the extension
            using var load = Image.Load(curImg);

            var format = load.Metadata.DecodedImageFormat;
            // return the image
            return (curImg.ToStream(), format?.FileExtensions.FirstOrDefault() ?? "png");
        }

        // get the image stream and extension
        return AddPassword(curImg, pass);
    }

    /// <summary>
    ///     Add a password to the image.
    /// </summary>
    /// <param name="curImg">Image to add password to.</param>
    /// <param name="pass">Password to add to top left corner.</param>
    /// <returns>Image with the password in the top left corner.</returns>
    private (Stream, string) AddPassword(byte[] curImg, string pass)
    {
        // draw lower, it looks better
        pass = pass.TrimTo(10, true).ToLowerInvariant();
        using var img = Image.Load<Rgba32>(curImg);
        // choose font size based on the image height, so that it's visible
        var font = fonts.NotoSans.CreateFont(img.Height / 11.0f, FontStyle.Bold);
        img.Mutate(x =>
        {
            // measure the size of the text to be drawing
            var size = TextMeasurer.MeasureSize(pass,
                new RichTextOptions(font)
                {
                    Origin = new PointF(0, 0)
                });

            // fill the background with black, add 5 pixels on each side to make it look better
            x.FillPolygon(Color.ParseHex("00000080"),
                new PointF(5, 5),
                new PointF(size.Width + 10, 5),
                new PointF(size.Width + 10, size.Height + 15),
                new PointF(5, size.Height + 15));

            var strikeoutRun = new RichTextRun
            {
                Start = 0,
                End = pass.GetGraphemeCount(),
                Font = font,
                StrikeoutPen = new SolidPen(Color.White, 2),
                TextDecorations = TextDecorations.Strikeout
            };

            // draw the password over the background
            x.DrawText(new RichTextOptions(font)
                {
                    Origin = new(5, 5),
                    TextRuns =
                    [
                        strikeoutRun
                    ]
                },
                pass,
                new SolidBrush(Color.White));
        });
        // return image as a stream for easy sending
        var format = img.Metadata.DecodedImageFormat;
        return (img.ToStream(format), format?.FileExtensions.FirstOrDefault() ?? "png");
    }

    private ValueTask PotentialFlowerGeneration(IUserMessage imsg)
    {
        if (imsg is not SocketUserMessage msg || msg.Author.IsBot)
            return default;

        if (imsg.Channel is not ITextChannel channel)
            return default;

        if (!_generationChannels.Contains(channel.Id))
            return default;

        _ = Task.Run(async () =>
        {
            try
            {
                var config = gss.Data;
                var lastGeneration = LastGenerations.GetOrAdd(channel.Id, DateTime.MinValue.ToBinary());
                var rng = new NadekoRandom();

                if (DateTime.UtcNow - TimeSpan.FromSeconds(config.Generation.GenCooldown)
                    < DateTime.FromBinary(lastGeneration)) //recently generated in this channel, don't generate again
                    return;

                var num = rng.Next(1, 101) + (config.Generation.Chance * 100);
                if (num > 100 && LastGenerations.TryUpdate(channel.Id, DateTime.UtcNow.ToBinary(), lastGeneration))
                {
                    var dropAmount = config.Generation.MinAmount;
                    var dropAmountMax = config.Generation.MaxAmount;

                    if (dropAmountMax > dropAmount)
                        dropAmount = new NadekoRandom().Next(dropAmount, dropAmountMax + 1);

                    if (dropAmount > 0)
                    {
                        var prefix = cmdHandler.GetPrefix(channel.Guild.Id);
                        var pw = config.Generation.HasPassword ? gs.GeneratePassword().ToUpperInvariant() : null;
                        var hasPw = !string.IsNullOrWhiteSpace(pw);

                        var pickLine = dropAmount == 1
                            ? hasPw ? strs.pick_sn_pw(prefix) : strs.pick_sn(prefix)
                            : hasPw ? strs.pick_pl_pw(prefix) : strs.pick_pl(prefix);

                        var toSend = dropAmount == 1
                            ? GetText(channel.GuildId, strs.curgen_sn(config.Currency.Sign))
                              + "\n> "
                              + GetText(channel.GuildId, pickLine)
                            : GetText(channel.GuildId, strs.curgen_pl(dropAmount, config.Currency.Sign))
                              + "\n> "
                              + GetText(channel.GuildId, pickLine);

                        IUserMessage sent;
                        var (stream, ext) = await GetRandomCurrencyImageAsync(pw);

                        await using (stream)
                            sent = await channel.SendFileAsync(stream, $"currency_image.{ext}", toSend);

                        var (total, toDelete) = await AddPlantToDatabase(channel.GuildId,
                            channel.Id,
                            client.CurrentUser.Id,
                            sent.Id,
                            dropAmount,
                            pw);

                        if (toDelete.Length > 0)
                        {
                            var merged = GetText(channel.GuildId, strs.curgen_pl(total, config.Currency.Sign))
                                         + "\n> "
                                         + GetText(channel.GuildId, strs.pick_pl_pw(prefix));

                            await sent.ModifyAsync(m => m.Content = merged);
                            await channel.DeleteMessagesAsync(toDelete);
                        }
                    }
                }
            }
            catch
            {
            }
        });
        return default;
    }

    public async Task<long> PickAsync(
        ulong gid,
        ITextChannel ch,
        ulong userId,
        string pass)
    {
        long amount;
        ulong[] ids;
        await using (var uow = db.GetDbContext())
        {
            // this method will sum all plants with that password,
            // remove them, and get messageids of the removed plants

            pass = pass?.Trim().TrimTo(10, true)?.ToUpperInvariant();
            // gets all plants in this channel with the same password
            var entries = await uow.GetTable<PlantedCurrency>()
                .Where(x => x.ChannelId == ch.Id && pass == x.Password)
                .DeleteWithOutputAsync();

            if (!entries.Any())
                return 0;

            amount = entries.Sum(x => x.Amount);
            ids = entries.Select(x => x.MessageId).ToArray();
        }

        if (amount > 0)
        {
            await cs.AddAsync(userId, amount, new("currency", "collect"));
            await quests.ReportActionAsync(userId,
                QuestEventType.PlantOrPick,
                new()
                {
                    { "type", "pick" },
                });
        }


        try
        {
            _ = ch.DeleteMessagesAsync(ids);
        }
        catch
        {
        }

        // return the amount of currency the user picked
        return amount;
    }

    private string GetPlantText(ulong gid, string user, long amount, string pass)
    {
        var prefix = cmdHandler.GetPrefix(gid);
        var text = GetText(gid,
            strs.planted(Format.Bold(user), CurrencyHelper.N(amount, CultureInfo.InvariantCulture, gss.Data.Currency.Sign)));

        var hasPw = !string.IsNullOrWhiteSpace(pass);
        if (amount > 1)
            return text + "\n> " + GetText(gid, hasPw ? strs.pick_pl_pw(prefix) : strs.pick_pl(prefix));

        return text + "\n> " + GetText(gid, hasPw ? strs.pick_sn_pw(prefix) : strs.pick_sn(prefix));
    }

    public async Task<IUserMessage> SendPlantMessageAsync(
        ulong gid,
        IMessageChannel ch,
        string user,
        long amount,
        string pass)
    {
        try
        {
            var (stream, ext) = await GetRandomCurrencyImageAsync(pass);
            await using (stream)
                return await ch.SendFileAsync(stream, $"img.{ext}", GetPlantText(gid, user, amount, pass));
        }
        catch (Exception ex)
        {
            // if sending fails, return null as message id
            Log.Warning(ex, "Sending plant message failed: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<bool> PlantAsync(
        ulong gid,
        ITextChannel ch,
        ulong userId,
        string user,
        long amount)
    {
        // a chosen password would let a bot plant, then pick every merged drop without reading an image
        var pass = gss.Data.Generation.HasPassword ? gs.GeneratePassword().ToUpperInvariant() : null;

        // remove currency from the user who's planting
        if (await cs.RemoveAsync(userId, amount, new("put/collect", "put")))
        {
            // try to send the message with the currency image
            var msg = await SendPlantMessageAsync(gid, ch, user, amount, pass);
            if (msg is null)
            {
                // if it fails it will return null, if it returns null, refund
                await cs.AddAsync(userId, amount, new("put/collect", "refund"));
                return false;
            }

            // if it doesn't fail, put the plant in the database for other people to pick
            var (total, toDelete) = await AddPlantToDatabase(gid, ch.Id, userId, msg.Id, amount, pass);
            await quests.ReportActionAsync(userId, QuestEventType.PlantOrPick, new() { { "type", "plant" } });

            if (toDelete.Length > 0)
            {
                try
                {
                    var merged = GetPlantText(gid, user, total, pass);
                    await msg.ModifyAsync(m => m.Content = merged);
                    await ch.DeleteMessagesAsync(toDelete);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Updating merged plant messages failed");
                }
            }

            return true;
        }

        // if user doesn't have enough currency, fail
        return false;
    }

    private async Task<(long totalAmount, ulong[] toDelete)> AddPlantToDatabase(
        ulong gid,
        ulong cid,
        ulong uid,
        ulong mid,
        long amount,
        string pass)
    {
        await using var uow = db.GetDbContext();
        await using var tran = await uow.Database.BeginTransactionAsync();

        // a new password drop takes over all earlier drops, otherwise drops which scrolled away stay unpicked forever
        PlantedCurrency[] deleted = [];
        if (!string.IsNullOrWhiteSpace(pass))
        {
            deleted = await uow.GetTable<PlantedCurrency>()
                .Where(x => x.GuildId == gid && x.ChannelId == cid)
                .DeleteWithOutputAsync();
        }

        var totalDeletedAmount = deleted.Length == 0 ? 0 : deleted.Sum(x => x.Amount);

        await uow.GetTable<PlantedCurrency>()
            .InsertAsync(() => new()
            {
                Amount = totalDeletedAmount + amount,
                GuildId = gid,
                ChannelId = cid,
                Password = pass,
                UserId = uid,
                MessageId = mid,
            });

        await tran.CommitAsync();

        return (totalDeletedAmount + amount, deleted.Select(x => x.MessageId).ToArray());
    }

    public async Task OnReadyAsync()
    {
        await using var uow = db.GetDbContext();
        _generationChannels = (await uow.GetTable<GCChannelId>()
                .Select(x => x.ChannelId)
                .ToListAsyncLinqToDB())
            .ToHashSet()
            .ToConcurrentSet();
    }
}