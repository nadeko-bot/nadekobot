using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Modules.Music;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

// The manager keeps the music tools current and can give the bot a proxy for YouTube. None of this shows to the
// bot's owner. Without these calls the bot uses the tools on PATH and connects to YouTube directly.
public static class MusicToolEndpoints
{
    public const string TOOL_VERSION_HEADER = "X-Tool-Version";
    public const string TOOL_DIGEST_HEADER = "X-Tool-Digest";

    public static RouteGroupBuilder MapMusicToolEndpoints(this RouteGroupBuilder system)
    {
        system.MapGet("/tools", ListTools);
        system.MapPut("/tools/{name}", InstallTool);
        system.MapDelete("/tools/{name}", RemoveTool);
        system.MapGet("/youtube", GetYoutube);
        system.MapPut("/youtube/proxy", SetYoutubeProxy);
        system.MapDelete("/youtube/proxy", ClearYoutubeProxy);
        system.MapGet("/music-service", GetMusicService);
        system.MapPut("/music-service", SetMusicService);
        system.MapDelete("/music-service", ClearMusicService);
        return system;
    }

    private static IResult ListTools(ToolService tools)
        => Results.Json(new ToolListDto(tools.List().Select(ToDto).ToArray()), ApiJsonContext.Default.ToolListDto);

    private static ToolDto ToDto(ToolManifest m)
        => new(m.Name, m.Version, m.Digest, m.InstalledAt);

    private static async Task<IResult> InstallTool(string name, HttpContext http, ApiSettings settings, ToolService tools)
    {
        if (!MediaTools.IsKnown(name))
            return ApiErrors.NotFound;

        var request = http.Request;
        if (!BackupEndpoints.HasMediaType(request.ContentType, ManagedEndpoints.GZIP_CONTENT_TYPE))
            return ApiErrors.Create(ApiErrors.UNSUPPORTED_MEDIA_TYPE, StatusCodes.Status415UnsupportedMediaType);

        var sha256 = request.Headers[ManagedEndpoints.SHA256_HEADER].ToString();
        var version = request.Headers[TOOL_VERSION_HEADER].ToString();
        var digest = request.Headers[TOOL_DIGEST_HEADER].ToString();
        if (!UpdateService.IsValidSha256(sha256)
            || version.Length > MediaTools.MAX_VERSION_LENGTH
            || !MediaTools.IsSafeSegment(version)
            || !MediaTools.IsValidDigest(digest))
            return ApiErrors.Create(ApiErrors.BAD_REQUEST, StatusCodes.Status400BadRequest);

        var limit = settings.MaxUpdateBytes;
        if (request.ContentLength > limit)
            return ApiErrors.Create(ApiErrors.TOO_LARGE, StatusCodes.Status413PayloadTooLarge);

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            sizeFeature.MaxRequestBodySize = limit;

        var result = await tools.InstallAsync(name,
            request.Body,
            request.ContentLength,
            sha256,
            version,
            digest.ToLowerInvariant(),
            http.RequestAborted);

        return result.Tool is { } tool
            ? Results.Json(ToDto(tool), ApiJsonContext.Default.ToolDto)
            : ApiErrors.Create(result.Error ?? ApiErrors.INTERNAL_ERROR, result.Status);
    }

    private static IResult RemoveTool(string name, ToolService tools)
    {
        if (!MediaTools.IsKnown(name))
            return ApiErrors.NotFound;

        return tools.TryRemove(name)
            ? Results.NoContent()
            : ApiErrors.Create(ApiErrors.TOOL_INSTALL_IN_PROGRESS, StatusCodes.Status409Conflict);
    }

    private static IResult GetYoutube(YoutubeUpstreamSetting proxy, YoutubeStats stats)
        => Results.Json(new YoutubeStatusDto(proxy.Id, ToDto(stats.LastHour()), ToDto(stats.LastDay())),
            ApiJsonContext.Default.YoutubeStatusDto);

    private static YoutubeCountsDto ToDto(YoutubeCounts c)
        => new(c.Ok, c.BotCheck, c.RateLimited, c.Forbidden, c.ProxyErrors, c.NoJsRuntime, c.OtherErrors, c.ProxyBytes);

    // The bot uses the new proxy from its next connection to YouTube; no restart.
    private static IResult SetYoutubeProxy(
        SetYoutubeProxyRequest body,
        YoutubeUpstreamSetting proxy,
        LogStreamService logs)
    {
        var id = body.Id;
        var url = body.Url;
        if (id is null
            || !YoutubeUpstreamSetting.IsValidId(id)
            || !UpstreamProxy.TryParse(url, out var parsed))
            return ApiErrors.InvalidValue;

        logs.AddSecret(parsed.Password);
        proxy.Set(id, url!);
        Log.Information("The YouTube proxy is now {ProxyId}", id);
        return Results.NoContent();
    }

    private static IResult ClearYoutubeProxy(YoutubeUpstreamSetting proxy)
    {
        proxy.Clear();
        Log.Information("The YouTube proxy was removed. YouTube traffic goes direct");
        return Results.NoContent();
    }

    // Never the key, which other bots share.
    private static IResult GetMusicService(MusicServiceSetting service)
    {
        var current = service.Current;
        return Results.Json(new MusicServiceDto(current?.Id, current?.BaseUrl), ApiJsonContext.Default.MusicServiceDto);
    }

    // The key becomes a log secret before the file exists, so no line which the bot writes with it shows the key.
    // The bot uses the service from its next search; no restart.
    private static IResult SetMusicService(
        SetMusicServiceRequest body,
        MusicServiceSetting service,
        LogStreamService logs)
    {
        if (!MusicServiceSetting.TryCreate(body.Id, body.Url, body.Key, out var entry))
            return ApiErrors.InvalidValue;

        logs.AddSecret(entry.Key);
        service.Set(entry);
        Log.Information("The music service is now {MusicServiceId}", entry.Id);
        return Results.NoContent();
    }

    private static IResult ClearMusicService(MusicServiceSetting service)
    {
        service.Clear();
        Log.Information("The music service was removed. YouTube music uses yt-dlp");
        return Results.NoContent();
    }
}
