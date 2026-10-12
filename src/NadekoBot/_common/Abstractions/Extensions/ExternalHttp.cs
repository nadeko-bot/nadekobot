using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace Nadeko.Common;

// The address check runs on each connection after DNS, so redirects and DNS rebinding cannot reach the local network.
public static class ExternalHttp
{
    public const string CLIENT_NAME = "external";

    // For URLs from the bot's config. A self-hosted owner may point them at the local network, a managed owner may not.
    public const string OWNER_CLIENT_NAME = "owner-config";
    public const int MAX_REDIRECTS = 3;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static SocketsHttpHandler CreateHandler()
        => new()
        {
            UseProxy = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MAX_REDIRECTS,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = ConnectPublicAsync
        };

    public static void Configure(HttpClient http)
        => http.Timeout = Timeout;

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var host = ctx.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        Exception? last = null;
        foreach (var address in addresses)
        {
            if (!UrlExtensions.IsPublicAddress(address))
            {
                last = new HttpRequestException($"{host} is not a public address");
                continue;
            }

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, ctx.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                socket.Dispose();
                last = ex;
            }
        }

        throw last ?? new HttpRequestException($"{host} has no address");
    }

    // Returns null when the body is larger than maxBytes. The caller returns the array to ArrayPool<byte>.Shared.
    public static async Task<(byte[] Buffer, int Length)?> ReadLimitedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken ct = default)
    {
        if (content.Headers.ContentLength > maxBytes)
            return null;

        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(maxBytes + 1, 64 * 1024));
        var length = 0;
        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    if (length > maxBytes)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        return null;
                    }

                    var bigger = ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length * 2, maxBytes + 1));
                    buffer.AsSpan(0, length).CopyTo(bigger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = bigger;
                }

                var read = await stream.ReadAsync(buffer.AsMemory(length), ct);
                if (read == 0)
                    break;

                length += read;
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        if (length > maxBytes)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            return null;
        }

        return (buffer, length);
    }

    public static async Task<MemoryStream?> ReadLimitedStreamAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken ct = default)
    {
        if (await ReadLimitedAsync(content, maxBytes, ct) is not { } d)
            return null;

        var copy = d.Buffer.AsSpan(0, d.Length).ToArray();
        ArrayPool<byte>.Shared.Return(d.Buffer);
        return new MemoryStream(copy, false);
    }

    public static async Task<byte[]?> GetBytesLimitedAsync(
        this HttpClient http,
        string url,
        int maxBytes,
        CancellationToken ct = default)
    {
        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode)
            return null;

        var data = await ReadLimitedAsync(res.Content, maxBytes, ct);
        if (data is not { } d)
            return null;

        try
        {
            return d.Buffer.AsSpan(0, d.Length).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(d.Buffer);
        }
    }
}
