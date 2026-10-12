namespace NadekoBot.Services;

public sealed class ImageCache : IImageCache, INService
{
    private readonly IBotCache _cache;
    private readonly ImagesConfig _ic;
    private readonly NadekoRandom _rng;
    private const int MAX_IMAGE_BYTES = 16 * 1024 * 1024;
    public const long MAX_IMAGE_PIXELS = 4096 * 4096;

    private readonly IHttpClientFactory _httpFactory;
    private readonly BotPolicy _policy;

    public ImageCache(
        IBotCache cache,
        ImagesConfig ic,
        IHttpClientFactory httpFactory,
        BotPolicy policy)
    {
        _policy = policy;
        _cache = cache;
        _ic = ic;
        _httpFactory = httpFactory;
        _rng = new NadekoRandom();
    }

    private static TypedKey<byte[]> GetImageKey(Uri url)
        => new($"image:{url}");

    public async Task<byte[]?> GetImageDataAsync(Uri url)
    {
        var data = await _cache.GetOrAddAsync(
            GetImageKey(url),
            async () =>
            {
                byte[]? bytes;
                if (url.IsFile)
                {
                    bytes = _policy.Managed ? null : await File.ReadAllBytesAsync(url.LocalPath);
                }
                else
                {
                    using var http = _httpFactory.CreateClient(ExternalHttp.OWNER_CLIENT_NAME);
                    bytes = await http.GetBytesLimitedAsync(url.ToString(), MAX_IMAGE_BYTES);
                }

                if (bytes is null)
                    return null;

                if (IsWithinPixelLimit(bytes))
                    return bytes;

                // An empty array marks a rejected image, so the bot does not download it again.
                Log.Warning("Image {Url} is too large to decode", url);
                return [];
            },
            expiry: TimeSpan.FromHours(48));

        return data is { Length: > 0 } ? data : null;
    }

    // A small file can declare a huge canvas or many frames, and the decoder allocates for all of them.
    public static bool IsWithinPixelLimit(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(bytes);
            var frames = Math.Max(1, info.FrameMetadataCollection.Count);
            return (long)info.Width * info.Height * frames <= MAX_IMAGE_PIXELS;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<byte[]?> GetRandomImageDataAsync(Uri[] urls)
    {
        if (urls.Length == 0)
            return null;

        var url = urls[_rng.Next(0, urls.Length)];

        var data = await GetImageDataAsync(url);
        return data;
    }

    public Task<byte[]?> GetHeadsImageAsync()
        => GetRandomImageDataAsync(_ic.Data.Coins.Heads);

    public Task<byte[]?> GetTailsImageAsync()
        => GetRandomImageDataAsync(_ic.Data.Coins.Tails);

    public Task<byte[]?> GetCurrencyImageAsync()
        => GetRandomImageDataAsync(_ic.Data.Currency);

    public Task<byte[]?> GetXpBackgroundImageAsync()
        => GetImageDataAsync(_ic.Data.Xp.Bg);

    public Task<byte[]?> GetDiceAsync(int num)
        => GetImageDataAsync(_ic.Data.Dice[num]);

    public Task<byte[]?> GetSlotEmojiAsync(int number)
        => GetImageDataAsync(_ic.Data.Slots.Emojis[number]);

    public Task<byte[]?> GetSlotBgAsync()
        => GetImageDataAsync(_ic.Data.Slots.Bg);
}