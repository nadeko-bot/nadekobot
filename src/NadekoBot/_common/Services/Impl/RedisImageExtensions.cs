#nullable disable
namespace NadekoBot.Services;

public static class RedisImageExtensions
{
    private const string OLD_CDN_URL = "nadeko-pictures.nyc3.digitaloceanspaces.com";
    private const string NEW_CDN_URL = "cdn.nadeko.bot";

    public static Uri ToNewCdn(this Uri uri)
        => new(uri.ToString().Replace(OLD_CDN_URL, NEW_CDN_URL));
}