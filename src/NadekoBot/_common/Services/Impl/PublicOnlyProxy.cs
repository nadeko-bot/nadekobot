using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NadekoBot.Services;

// A loopback HTTP proxy for yt-dlp, ffmpeg and the music cache. Those programs follow redirects and resolve DNS
// themselves, so a check of the first URL cannot stop them. The proxy checks the address of each connection
// after DNS and connects only to the address it checked.
public sealed class PublicOnlyProxy : IWebProxy, INService, IDisposable
{
    private const int MAX_CONNECTIONS = 256;
    private const int MAX_HEADER_BYTES = 16 * 1024;
    private const int BUFFER_SIZE = 16 * 1024;
    private const int LISTEN_BACKLOG = 512;

    private static readonly TimeSpan _headerTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

    // A paused song keeps its stream open without traffic; ffmpeg reconnects after the timeout.
    private static readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(5);

    private static readonly byte[] _headEnd = "\r\n\r\n"u8.ToArray();
    private static readonly byte[] _connectOk = "HTTP/1.1 200 Connection established\r\n\r\n"u8.ToArray();
    private static readonly byte[] _badRequest = CreateStatus("400 Bad Request");
    private static readonly byte[] _forbidden = CreateStatus("403 Forbidden");
    private static readonly byte[] _unavailable = CreateStatus("503 Service Unavailable");

    private static readonly string[] _droppedHeaders =
        ["Proxy-Connection", "Proxy-Authorization", "Connection", "Keep-Alive"];

    private static readonly string[] _proxyEnvVars =
        ["HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy", "ALL_PROXY", "all_proxy"];

    // The environment variables which make yt-dlp or ffmpeg skip a proxy for some hosts.
    public static readonly string[] BypassEnvVars = ["no_proxy", "NO_PROXY"];

    private readonly Func<IPAddress, bool> _isAllowed;
    private readonly DnsEndPoint? _upstream;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _startLock = new();
    private TcpListener? _listener;
    private volatile string? _url;
    private int _connections;

    // A managed bot ignores the environment. A self-hosted bot keeps the HTTP proxy which some owners use for YouTube.
    public PublicOnlyProxy(BotPolicy policy)
        : this(UrlExtensions.IsPublicAddress, policy.Managed ? null : ReadUpstreamFromEnvironment())
    {
    }

    private PublicOnlyProxy(Func<IPAddress, bool> isAllowed, DnsEndPoint? upstream)
    {
        _isAllowed = isAllowed;
        _upstream = upstream;
    }

    public static PublicOnlyProxy WithAddressCheck(Func<IPAddress, bool> isAllowed)
        => new(isAllowed, null);

    public ICredentials? Credentials { get; set; }

    public Uri GetProxy(Uri destination)
        => TryGetUrl(out var url)
            ? new Uri(url)
            : throw new InvalidOperationException("The public-only proxy is not available");

    public bool IsBypassed(Uri host)
        => false;

    public bool TryGetUrl(out string url)
    {
        if (_url is { } existing)
        {
            url = existing;
            return true;
        }

        lock (_startLock)
        {
            if (_url is null)
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start(LISTEN_BACKLOG);
                    _listener = listener;
                    _url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
                    _ = Task.Run(AcceptLoopInternalAsync);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unable to start the public-only proxy");
                    url = string.Empty;
                    return false;
                }
            }

            url = _url;
            return true;
        }
    }

    private static DnsEndPoint? ReadUpstreamFromEnvironment()
    {
        foreach (var name in _proxyEnvVars)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp)
                return new DnsEndPoint(uri.IdnHost, uri.Port);

            Log.Warning("Ignoring {Name}: only an http:// proxy can carry music and yt-dlp traffic", name);
            return null;
        }

        return null;
    }

    private async Task AcceptLoopInternalAsync()
    {
        var listener = _listener!;
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptSocketAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Public-only proxy failed to accept a connection");
                await Task.Delay(100, CancellationToken.None);
                continue;
            }

            if (Interlocked.Increment(ref _connections) > MAX_CONNECTIONS)
            {
                Interlocked.Decrement(ref _connections);
                Log.Warning("Public-only proxy has {Max} open connections, refusing a new one", MAX_CONNECTIONS);
                _ = RefuseInternalAsync(client, _unavailable);
                continue;
            }

            _ = Task.Run(() => HandleInternalAsync(client));
        }
    }

    private static async Task RefuseInternalAsync(Socket client, byte[] response)
    {
        try
        {
            await using var stream = new NetworkStream(client, ownsSocket: true);
            await stream.WriteAsync(response);
        }
        catch
        {
            // the client is gone
        }
    }

    private async Task HandleInternalAsync(Socket client)
    {
        var buf = ArrayPool<byte>.Shared.Rent(MAX_HEADER_BYTES);
        Socket? server = null;
        try
        {
            await using var clientStream = new NetworkStream(client, ownsSocket: true);
            using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            headerCts.CancelAfter(_headerTimeout);

            var (headLength, total) = await ReadHeadInternalAsync(clientStream, buf, headerCts.Token);
            if (headLength < 0 || !TryParseRequest(buf.AsSpan(0, headLength), out var request))
            {
                await clientStream.WriteAsync(_badRequest, headerCts.Token);
                return;
            }

            server = await ConnectCheckedInternalAsync(request.Host, request.Port, headerCts.Token);
            if (server is null)
            {
                Log.Debug("Public-only proxy refused {Host}:{Port}", request.Host, request.Port);
                await clientStream.WriteAsync(_forbidden, headerCts.Token);
                return;
            }

            await using var serverStream = new NetworkStream(server, ownsSocket: false);
            if (request.IsConnect)
                await clientStream.WriteAsync(_connectOk, headerCts.Token);
            else
                await serverStream.WriteAsync(BuildOriginRequest(buf.AsSpan(0, headLength), request), headerCts.Token);

            if (total > headLength)
                await serverStream.WriteAsync(buf.AsMemory(headLength, total - headLength), headerCts.Token);

            await RelayInternalAsync(clientStream, serverStream, server);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Public-only proxy connection failed");
        }
        finally
        {
            server?.Dispose();
            ArrayPool<byte>.Shared.Return(buf);
            Interlocked.Decrement(ref _connections);
        }
    }

    // Returns the length of the head with its blank line, and the count of bytes read, which can include body bytes.
    private static async Task<(int HeadLength, int Total)> ReadHeadInternalAsync(
        Stream stream,
        byte[] buf,
        CancellationToken ct)
    {
        var total = 0;
        while (total < MAX_HEADER_BYTES)
        {
            var read = await stream.ReadAsync(buf.AsMemory(total, MAX_HEADER_BYTES - total), ct);
            if (read == 0)
                return (-1, total);

            var searchFrom = Math.Max(0, total - (_headEnd.Length - 1));
            total += read;
            var idx = buf.AsSpan(searchFrom, total - searchFrom).IndexOf(_headEnd);
            if (idx >= 0)
                return (searchFrom + idx + _headEnd.Length, total);
        }

        return (-1, total);
    }

    public readonly record struct ProxyRequest(bool IsConnect, string Host, int Port, string Method, string Path);

    public static bool TryParseRequest(ReadOnlySpan<byte> head, out ProxyRequest request)
    {
        request = default;
        var lineEnd = head.IndexOf("\r\n"u8);
        if (lineEnd <= 0)
            return false;

        var line = Encoding.ASCII.GetString(head[..lineEnd]);
        var parts = line.Split(' ');
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            return false;

        var method = parts[0];
        var target = parts[1];

        if (string.Equals(method, "CONNECT", StringComparison.Ordinal))
        {
            if (!TrySplitHostPort(target, out var host, out var port))
                return false;

            request = new(true, host, port, method, string.Empty);
            return true;
        }

        // An https URL always comes through CONNECT; a plain request must be absolute-form http.
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
            return false;

        request = new(false, uri.IdnHost, uri.Port, method, uri.PathAndQuery);
        return true;
    }

    private static bool TrySplitHostPort(string target, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        int colon;
        if (target.StartsWith('['))
        {
            var close = target.IndexOf(']');
            if (close < 0 || close + 1 >= target.Length || target[close + 1] != ':')
                return false;

            host = target[1..close];
            colon = close + 1;
        }
        else
        {
            colon = target.LastIndexOf(':');
            if (colon <= 0)
                return false;

            host = target[..colon];
        }

        return int.TryParse(target.AsSpan(colon + 1), out port) && port is > 0 and <= IPEndPoint.MaxPort;
    }

    private static byte[] BuildOriginRequest(ReadOnlySpan<byte> head, in ProxyRequest request)
    {
        var text = Encoding.ASCII.GetString(head);
        var lines = text.Split("\r\n");
        var version = lines[0][(lines[0].LastIndexOf(' ') + 1)..];

        var sb = new StringBuilder(text.Length + 32);
        sb.Append(request.Method).Append(' ').Append(request.Path).Append(' ').Append(version).Append("\r\n");
        for (var i = 1; i < lines.Length; i++)
        {
            var header = lines[i];
            if (header.Length == 0)
                continue;

            var colon = header.IndexOf(':');
            if (colon > 0 && IsDroppedHeader(header.AsSpan(0, colon).Trim()))
                continue;

            sb.Append(header).Append("\r\n");
        }

        sb.Append("Connection: close\r\n\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static bool IsDroppedHeader(ReadOnlySpan<char> name)
    {
        // Header names are ASCII.
        foreach (var dropped in _droppedHeaders)
        {
            if (name.Equals(dropped, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task<Socket?> ConnectCheckedInternalAsync(string host, int port, CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException)
        {
            return null;
        }

        foreach (var address in addresses)
        {
            if (!_isAllowed(address))
                continue;

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(_connectTimeout);

            var socket = _upstream is { } upstream
                ? await ConnectViaUpstreamInternalAsync(upstream, address, port, connectCts.Token)
                : await ConnectDirectInternalAsync(address, port, connectCts.Token);

            if (socket is not null)
                return socket;
        }

        return null;
    }

    private static async Task<Socket?> ConnectDirectInternalAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), ct);
            return socket;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            socket.Dispose();
            return null;
        }
    }

    // The upstream proxy gets the checked address, so it cannot resolve the name to a different one.
    private static async Task<Socket?> ConnectViaUpstreamInternalAsync(
        DnsEndPoint upstream,
        IPAddress address,
        int port,
        CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        var buf = ArrayPool<byte>.Shared.Rent(MAX_HEADER_BYTES);
        try
        {
            await socket.ConnectAsync(upstream, ct);
            var target = address.AddressFamily == AddressFamily.InterNetworkV6
                ? $"[{address}]:{port}"
                : $"{address}:{port}";

            await using var stream = new NetworkStream(socket, ownsSocket: false);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT {target} HTTP/1.1\r\nHost: {target}\r\n\r\n"), ct);

            var (headLength, total) = await ReadHeadInternalAsync(stream, buf, ct);
            if (headLength < 0 || total != headLength || !IsStatus200(buf.AsSpan(0, headLength)))
            {
                socket.Dispose();
                return null;
            }

            return socket;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            socket.Dispose();
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static bool IsStatus200(ReadOnlySpan<byte> head)
    {
        var space = head.IndexOf((byte)' ');
        return space > 0 && head.Length >= space + 4 && head.Slice(space + 1, 3).SequenceEqual("200"u8);
    }

    private static async Task RelayInternalAsync(Stream client, Stream server, Socket serverSocket)
    {
        using var idle = new CancellationTokenSource(_idleTimeout);
        var up = PumpInternalAsync(client, server, idle);
        var down = PumpInternalAsync(server, client, idle);

        // A client which closes its sending side still waits for the response.
        if (await Task.WhenAny(up, down) == up && up.IsCompletedSuccessfully)
        {
            try
            {
                serverSocket.Shutdown(SocketShutdown.Send);
            }
            catch (SocketException)
            {
            }

            try
            {
                await down;
            }
            catch
            {
                // closed or timed out
            }
        }

        await idle.CancelAsync();
        try
        {
            await Task.WhenAll(up, down);
        }
        catch
        {
            // closed or timed out
        }
    }

    private static async Task PumpInternalAsync(Stream from, Stream to, CancellationTokenSource idle)
    {
        var buf = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
        try
        {
            while (true)
            {
                var read = await from.ReadAsync(buf.AsMemory(0, BUFFER_SIZE), idle.Token);
                if (read == 0)
                    return;

                await to.WriteAsync(buf.AsMemory(0, read), idle.Token);
                idle.CancelAfter(_idleTimeout);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static byte[] CreateStatus(string status)
        => Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        _cts.Dispose();
    }
}
