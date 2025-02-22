#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;

namespace NadekoBot.Modules.Gambling.Services;

public class PlantPickService : INService, IExecNoCommand, IReadyExecutor
{
    //channelId/last generation
    public ConcurrentDictionary<ulong, long> LastGenerations { get; } = new();
    private readonly DbService _db;
    private readonly IBotStrings _strings;
    private readonly IImageCache _images;
    private readonly FontProvider _fonts;
    private readonly ICurrencyService _cs;
    private readonly CommandHandler _cmdHandler;
    private readonly NadekoRandom _rng;
    private readonly DiscordSocketClient _client;
    private readonly GamblingConfigService _gss;
    private readonly GamblingService _gs;

    private ConcurrentHashSet<ulong> _generationChannels;
    private readonly SemaphoreSlim _pickLock = new(1, 1);

    public PlantPickService(
        DbService db,
        IBotStrings strings,
        IImageCache images,
        FontProvider fonts,
        ICurrencyService cs,
        CommandHandler cmdHandler,
        DiscordSocketClient client,
        GamblingConfigService gss,
        GamblingService gs)
    {
        _db = db;
        _strings = strings;
        _images = images;
        _fonts = fonts;
        _cs = cs;
        _cmdHandler = cmdHandler;
        _rng = new();
        _client = client;
        _gss = gss;
        _gs = gs;

        using var uow = db.GetDbContext();
        var guildIds = client.Guilds.Select(x => x.Id).ToList();
    }

    public Task ExecOnNoCommandAsync(IGuild guild, IUserMessage msg)
        => PotentialFlowerGeneration(msg);

    private string GetText(ulong gid, LocStr str)
        => _strings.GetText(str, gid);

    public async Task<bool> ToggleCurrencyGeneration(ulong gid, ulong cid)
    {
        bool enabled;
        await using var uow = _db.GetDbContext();

        if (_generationChannels.Add(cid))
        {
            await uow.GetTable<GCChannelId>()
                    .InsertOrUpdateAsync(() => new()
                    {
                        ChannelId = cid,
                        GuildId = gid
                    }, (x) => new()
                    {
                        ChannelId = cid,
                        GuildId = gid
                    }, () => new()
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
        await using var uow = _db.GetDbContext();
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
        var curImg = await _images.GetCurrencyImageAsync();

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
        var font = _fonts.NotoSans.CreateFont(img.Height / 11.0f, FontStyle.Bold);
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
                new PointF(1, 1),
                new PointF(size.Width + 5, 0),
                new PointF(size.Width + 5, size.Height + 10),
                new PointF(0, size.Height + 10));

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
                Origin = new(0, 0),
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

    private Task PotentialFlowerGeneration(IUserMessage imsg)
    {
        if (imsg is not SocketUserMessage msg || msg.Author.IsBot)
            return Task.CompletedTask;

        if (imsg.Channel is not ITextChannel channel)
            return Task.CompletedTask;

        if (!_generationChannels.Contains(channel.Id))
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            try
            {
                var config = _gss.Data;
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
                        var prefix = _cmdHandler.GetPrefix(channel.Guild.Id);
                        var toSend = dropAmount == 1
                            ? GetText(channel.GuildId, strs.curgen_sn(config.Currency.Sign))
                              + " "
                              + GetText(channel.GuildId, strs.pick_sn(prefix))
                            : GetText(channel.GuildId, strs.curgen_pl(dropAmount, config.Currency.Sign))
                              + " "
                              + GetText(channel.GuildId, strs.pick_pl(prefix));

                        var pw = config.Generation.HasPassword ? _gs.GeneratePassword().ToUpperInvariant() : null;

                        IUserMessage sent;
                        var (stream, ext) = await GetRandomCurrencyImageAsync(pw);

                        await using (stream)
                            sent = await channel.SendFileAsync(stream, $"currency_image.{ext}", toSend);

                        var res = await AddPlantToDatabase(channel.GuildId,
                            channel.Id,
                            _client.CurrentUser.Id,
                            sent.Id,
                            dropAmount,
                            pw,
                            true);

                        if (res.toDelete.Length > 0)
                        {
                            await channel.DeleteMessagesAsync(res.toDelete);
                        }
                    }
                }
            }
            catch
            {
            }
        });
        return Task.CompletedTask;
    }

    public async Task<long> PickAsync(
        ulong gid,
        ITextChannel ch,
        ulong uid,
        string pass)
    {
        long amount;
        ulong[] ids;
        await using (var uow = _db.GetDbContext())
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
            await _cs.AddAsync(uid, amount, new("currency", "collect"));


        try
        {
            _ = ch.DeleteMessagesAsync(ids);
        }
        catch { }

        // return the amount of currency the user picked
        return amount;
    }

    public async Task<ulong?> SendPlantMessageAsync(
        ulong gid,
        IMessageChannel ch,
        string user,
        long amount,
        string pass)
    {
        try
        {
            // get the text
            var prefix = _cmdHandler.GetPrefix(gid);
            var msgToSend = GetText(gid, strs.planted(Format.Bold(user), amount + _gss.Data.Currency.Sign));

            if (amount > 1)
                msgToSend += " " + GetText(gid, strs.pick_pl(prefix));
            else
                msgToSend += " " + GetText(gid, strs.pick_sn(prefix));

            //get the image
            var (stream, ext) = await GetRandomCurrencyImageAsync(pass);
            // send it
            await using (stream)
            {
                var msg = await ch.SendFileAsync(stream, $"img.{ext}", msgToSend);
                // return sent message's id (in order to be able to delete it when it's picked)
                return msg.Id;
            }
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
        ulong uid,
        string user,
        long amount,
        string pass)
    {
        // normalize it - no more than 10 chars, uppercase
        pass = pass?.Trim().TrimTo(10, true).ToUpperInvariant();
        // has to be either null or alphanumeric
        if (!string.IsNullOrWhiteSpace(pass) && !pass.IsAlphaNumeric())
            return false;

        // remove currency from the user who's planting
        if (await _cs.RemoveAsync(uid, amount, new("put/collect", "put")))
        {
            // try to send the message with the currency image
            var msgId = await SendPlantMessageAsync(gid, ch, user, amount, pass);
            if (msgId is null)
            {
                // if it fails it will return null, if it returns null, refund
                await _cs.AddAsync(uid, amount, new("put/collect", "refund"));
                return false;
            }

            // if it doesn't fail, put the plant in the database for other people to pick
            await AddPlantToDatabase(gid, ch.Id, uid, msgId.Value, amount, pass);

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
        string pass,
        bool auto = false)
    {
        await using var uow = _db.GetDbContext();

        PlantedCurrency[] deleted = [];
        if (!string.IsNullOrWhiteSpace(pass) && auto)
        {
            deleted = await uow.GetTable<PlantedCurrency>()
                               .Where(x => x.GuildId == gid
                                           && x.ChannelId == cid
                                           && x.Password != null
                                           && x.Password.Length == pass.Length)
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

        return (totalDeletedAmount + amount, deleted.Select(x => x.MessageId).ToArray());
    }

    public async Task OnReadyAsync()
    {

        await using var uow = _db.GetDbContext();
        _generationChannels = (await uow.GetTable<GCChannelId>()
            .Select(x => x.ChannelId)
            .ToListAsyncLinqToDB())
            .ToHashSet()
            .ToConcurrentSet();

    }
}