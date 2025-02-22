using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Modules.Games;

public sealed class NCanvasService : INCanvasService, IReadyExecutor, INService
{
    private readonly TypedKey<uint[]> _canvasKey = new("ncanvas");

    private readonly DbService _db;
    private readonly IBotCache _cache;
    private readonly DiscordSocketClient _client;
    private readonly ICurrencyService _cs;

    public const int CANVAS_WIDTH = 500;
    public const int CANVAS_HEIGHT = 350;
    public const int INITIAL_PRICE = 3;

    public NCanvasService(
        DbService db,
        IBotCache cache,
        DiscordSocketClient client,
        ICurrencyService cs)
    {
        _db = db;
        _cache = cache;
        _client = client;
        _cs = cs;
    }

    public async Task OnReadyAsync()
    {
        if (_client.ShardId != 0)
            return;

        await using var uow = _db.GetDbContext();

        if (await uow.GetTable<NCPixel>().CountAsyncLinqToDB() > 0)
            return;

        await ResetAsync();
    }

    public async Task ResetAsync()
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<NCPixel>().DeleteAsync();

        var toAdd = new List<int>();
        for (var i = 0; i < CANVAS_WIDTH * CANVAS_HEIGHT; i++)
        {
            toAdd.Add(i);
        }

        await uow.GetTable<NCPixel>()
                 .BulkCopyAsync(toAdd.Select(x =>
                 {
                     var clr = ColorSpaceConverter.ToRgb(new Hsv(((float)Random.Shared.NextDouble() * 360),
                                                      (float)(0.5 + (Random.Shared.NextDouble() * 0.49)),
                                                      (float)(0.4 + (Random.Shared.NextDouble() / 5 + (x % 100 * 0.2)))))
                                                  .ToVector3();

                     var packed = new Rgba32(clr).PackedValue;
                     return new NCPixel()
                     {
                         Color = packed,
                         Price = 1,
                         Position = x,
                         Text = "",
                         OwnerId = 0
                     };
                 }));
    }


    private async Task<uint[]> InternalGetCanvas()
    {
        await using var uow = _db.GetDbContext();
        var colors = await uow.GetTable<NCPixel>()
                              .OrderBy(x => x.Position)
                              .Select(x => x.Color)
                              .ToArrayAsyncLinqToDB();

        return colors;
    }

    public async Task<uint[]> GetCanvas()
    {
        return await _cache.GetOrAddAsync(_canvasKey,
                   async () => await InternalGetCanvas(),
                   TimeSpan.FromSeconds(15))
               ?? [];
    }

    public async Task<SetPixelResult> SetPixel(
        int position,
        uint color,
        string text,
        ulong userId,
        long price)
    {
        if (position < 0 || position >= CANVAS_WIDTH * CANVAS_HEIGHT)
            return SetPixelResult.InvalidInput;

        var wallet = await _cs.GetWalletAsync(userId);

        var paid = await wallet.Take(price, new("canvas", "pixel-buy", $"Bought pixel {new kwum(position)}"));
        if (!paid)
        {
            return SetPixelResult.NotEnoughMoney;
        }

        var success = false;
        try
        {
            await using var uow = _db.GetDbContext();
            var updates = await uow.GetTable<NCPixel>()
                                   .Where(x => x.Position == position && x.Price <= price)
                                   .UpdateAsync(old => new NCPixel()
                                   {
                                       Position = position,
                                       Color = color,
                                       Text = text,
                                       OwnerId = userId,
                                       Price = price + 1
                                   });
            success = updates > 0;
        }
        catch
        {
        }

        if (!success)
        {
            await wallet.Add(price, new("canvas", "pixel-refund", $"Refund pixel {new kwum(position)} purchase"));
        }

        return success ? SetPixelResult.Success : SetPixelResult.InsufficientPayment;
    }

    public async Task<bool> SetImage(uint[] colors)
    {
        if (colors.Length != CANVAS_WIDTH * CANVAS_HEIGHT)
            return false;

        await using var uow = _db.GetDbContext();
        await uow.GetTable<NCPixel>().DeleteAsync();
        await uow.GetTable<NCPixel>()
                 .BulkCopyAsync(colors.Select((x, i) => new NCPixel()
                 {
                     Color = x,
                     Price = INITIAL_PRICE,
                     Position = i,
                     Text = "",
                     OwnerId = 0
                 }));

        return true;
    }

    public Task<NCPixel?> GetPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);

        if (x >= CANVAS_WIDTH || y >= CANVAS_HEIGHT)
            return Task.FromResult<NCPixel?>(null);

        return GetPixel(x + (y * CANVAS_WIDTH));
    }

    public async Task<NCPixel?> GetPixel(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);

        await using var uow = _db.GetDbContext();
        return await uow.GetTable<NCPixel>().FirstOrDefaultAsync(x => x.Position == position);
    }

    public async Task<NCPixel[]> GetPixelGroup(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, CANVAS_WIDTH * CANVAS_HEIGHT);

        await using var uow = _db.GetDbContext();
        return await uow.GetTable<NCPixel>()
                        .Where(x => x.Position % CANVAS_WIDTH >= (position % CANVAS_WIDTH) - 2
                                    && x.Position % CANVAS_WIDTH <= (position % CANVAS_WIDTH) + 2
                                    && x.Position / CANVAS_WIDTH >= (position / CANVAS_WIDTH) - 2
                                    && x.Position / CANVAS_WIDTH <= (position / CANVAS_WIDTH) + 2)
                        .OrderBy(x => x.Position)
                        .ToArrayAsyncLinqToDB();
    }

    public int GetHeight()
        => CANVAS_HEIGHT;

    public int GetWidth()
        => CANVAS_WIDTH;
}