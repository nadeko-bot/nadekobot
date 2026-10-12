using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace NadekoBot.Hosted;

[Flags]
public enum ApiAccess
{
    Open = 0,
    Key = 1,
    Bot = 2,
    Guild = 4,
    KeyOnceSet = 8
}

public sealed record ApiAccessMetadata(ApiAccess Access);

public static class BearerAuth
{
    private const string PREFIX = "Bearer ";
    private const int STACK_LIMIT = 256;

    // Compares SHA-256 hashes, so the time taken does not depend on the token or reveal the key length.
    public static bool Matches(ReadOnlySpan<char> header, ReadOnlySpan<byte> keyHash)
    {
        // The scheme is case-insensitive (RFC 9110 11.1) and ASCII.
        if (keyHash.IsEmpty || !header.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase))
            return false;

        // RFC 9110 allows more than one space after the scheme.
        var token = header[PREFIX.Length..].TrimStart(' ');
        var byteCount = Encoding.UTF8.GetByteCount(token);

        byte[]? rented = null;
        var buffer = byteCount <= STACK_LIMIT
            ? stackalloc byte[STACK_LIMIT]
            : rented = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var written = Encoding.UTF8.GetBytes(token, buffer);
            Span<byte> tokenHash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(buffer[..written], tokenHash);
            return CryptographicOperations.FixedTimeEquals(tokenHash, keyHash);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }
}

public sealed class ApiGateMiddleware(RequestDelegate next, ApiKey key, BotBridge bridge, HostContext host)
{
    public const string GUILD_ID_ROUTE_VALUE = "guildId";

    public Task InvokeAsync(HttpContext http)
    {
        // Routing's own endpoints (a 405) and unknown paths carry no metadata; the key keeps them from mapping the routes.
        var access = http.GetEndpoint()?.Metadata.GetMetadata<ApiAccessMetadata>()?.Access ?? ApiAccess.KeyOnceSet;
        if (access == ApiAccess.Open)
            return next(http);

        var needsKey = (access & ApiAccess.Key) != 0
                       || ((access & ApiAccess.KeyOnceSet) != 0 && key.IsSet);

        if (needsKey && !key.Matches(http.Request.Headers.Authorization.ToString()))
            return ApiErrors.WriteAsync(http, StatusCodes.Status401Unauthorized, ApiErrors.UNAUTHORIZED);

        ulong guildId = 0;
        if ((access & ApiAccess.Guild) != 0)
        {
            if (!TryGetGuildIdInternal(http, out guildId))
                return ApiErrors.WriteAsync(http, StatusCodes.Status404NotFound, ApiErrors.NOT_FOUND);

            if (!host.OwnsGuild(guildId))
                return ApiErrors.WriteAsync(http, StatusCodes.Status421MisdirectedRequest, ApiErrors.WRONG_SHARD);
        }

        if ((access & ApiAccess.Bot) != 0 && bridge.Phase != BotPhase.Ready)
            return ApiErrors.WriteAsync(http, StatusCodes.Status503ServiceUnavailable, ApiErrors.NOT_READY);

        if ((access & ApiAccess.Guild) != 0)
        {
            if (bridge.Client?.GetGuild(guildId) is not { } guild)
                return ApiErrors.WriteAsync(http, StatusCodes.Status404NotFound, ApiErrors.NOT_FOUND);

            // A Discord outage for this guild; its cache is empty until it comes back.
            if (!((IGuild)guild).Available)
                return ApiErrors.WriteAsync(http, StatusCodes.Status503ServiceUnavailable, ApiErrors.NOT_READY);

            GuildRef.Set(http, guild);
        }

        return next(http);
    }

    private static bool TryGetGuildIdInternal(HttpContext http, out ulong guildId)
    {
        guildId = 0;
        return http.Request.RouteValues.TryGetValue(GUILD_ID_ROUTE_VALUE, out var raw)
               && raw is string text
               && ApiLimits.TryParseId(text, out guildId);
    }
}
