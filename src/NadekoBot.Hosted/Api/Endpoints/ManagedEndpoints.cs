using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class ManagedEndpoints
{
    public const string GZIP_CONTENT_TYPE = "application/gzip";
    public const string SHA256_HEADER = "X-Content-Sha256";
    public const string VERSION_HEADER = "X-Bot-Version";

    public static RouteGroupBuilder MapCredsEndpoint(this RouteGroupBuilder creds)
    {
        creds.MapPut("/creds", SetCreds);
        return creds;
    }

    public static RouteGroupBuilder MapAiKeyEndpoints(this RouteGroupBuilder owner)
    {
        owner.MapGet("/ai-key", GetAiKey);
        owner.MapPut("/ai-key", SetAiKey);
        owner.MapDelete("/ai-key", ClearAiKey);
        return owner;
    }

    private static IResult GetAiKey(IBotCredsProvider credsProvider)
        => Results.Json(
            new AiKeyStatusDto(!string.IsNullOrWhiteSpace(credsProvider.GetCreds().AiApiKey)),
            ApiJsonContext.Default.AiKeyStatusDto);

    private static async Task<IResult> SetAiKey(
        SetAiKeyRequest body,
        CredsStore creds,
        IBotCredsProvider credsProvider)
    {
        var key = body.Key?.Trim();
        if (key is null || !CredsFile.IsValidAiKey(key))
            return ApiErrors.InvalidValue;

        await creds.SetAiKeyAsync(key);
        credsProvider.Reload();
        return Results.NoContent();
    }

    private static async Task<IResult> ClearAiKey(CredsStore creds, IBotCredsProvider credsProvider)
    {
        await creds.SetAiKeyAsync(null);
        credsProvider.Reload();
        return Results.NoContent();
    }

    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder system)
    {
        system.MapPost("/system/restart", Restart);
        system.MapGet("/system/disk", GetDisk);
        return system;
    }

    public static RouteGroupBuilder MapUpdateEndpoint(this RouteGroupBuilder system)
    {
        system.MapPut("/update", Update);
        return system;
    }

    private static async Task<IResult> Update(
        HttpContext http,
        ApiSettings settings,
        UpdateService updates,
        IProcessRestarter restarter)
    {
        var request = http.Request;
        if (!BackupEndpoints.HasMediaType(request.ContentType, GZIP_CONTENT_TYPE))
            return ApiErrors.Create(ApiErrors.UNSUPPORTED_MEDIA_TYPE, StatusCodes.Status415UnsupportedMediaType);

        var sha256 = request.Headers[SHA256_HEADER].ToString();
        var version = request.Headers[VERSION_HEADER].ToString();
        if (!UpdateService.IsValidSha256(sha256) || !UpdateService.TryParseVersion(version, out var parsed))
            return ApiErrors.Create(ApiErrors.BAD_REQUEST, StatusCodes.Status400BadRequest);

        if (parsed < UpdateService.MinVersion)
            return ApiErrors.InvalidValue;

        var limit = settings.MaxUpdateBytes;
        if (request.ContentLength > limit)
            return ApiErrors.Create(ApiErrors.TOO_LARGE, StatusCodes.Status413PayloadTooLarge);

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            sizeFeature.MaxRequestBodySize = limit;

        var result = await updates.StageAsync(request.Body, request.ContentLength, sha256, version, http.RequestAborted);
        if (result.Version is not { } staged)
            return ApiErrors.Create(result.Error ?? ApiErrors.INTERNAL_ERROR, result.Status);

        restarter.RestartAfter(http);
        return Results.Json(new UpdateAcceptedDto(staged), ApiJsonContext.Default.UpdateAcceptedDto,
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> SetCreds(
        SetCredsRequest body,
        HttpContext http,
        CredsStore creds,
        IProcessRestarter restarter)
    {
        // The key is not trimmed: the manager must send exactly the key it will use.
        var token = body.Token?.Trim();
        var apiKey = body.ApiKey;
        ulong[]? owners = null;
        if (!ApiSettings.IsValidKey(apiKey)
            || (token is not null && !CredsFile.IsValidToken(token))
            || (body.OwnerIds is not null && !OwnerIdList.TryParse(body.OwnerIds, out owners)))
            return ApiErrors.InvalidValue;

        if (!await creds.TrySetAsync(http.Request.Headers.Authorization.ToString(), apiKey, token, owners))
            return ApiErrors.Unauthorized;

        Log.Information("Credentials were updated over the API. Restarting to apply them");
        restarter.RestartAfter(http);
        return Results.Accepted();
    }

    private static IResult GetDisk(HostPaths paths)
        => ReadDiskUsage(paths.DataDir) is { } usage
            ? Results.Json(usage, ApiJsonContext.Default.DiskUsageDto)
            : ApiErrors.Create(ApiErrors.DISK_UNAVAILABLE, StatusCodes.Status503ServiceUnavailable);

    public static DiskUsageDto? ReadDiskUsage(string dataDir)
    {
        try
        {
            // In a container the data folder is a symlink to its own volume, so measure the volume it points to.
            var dir = new DirectoryInfo(dataDir);
            var real = dir.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? dir.FullName;
            var drive = new DriveInfo(real);
            var total = drive.TotalSize;
            if (total <= 0)
                return null;

            return new(Math.Clamp(total - drive.AvailableFreeSpace, 0, total), total);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warning(ex, "Unable to read the size of the data disk");
            return null;
        }
    }

    private static IResult Restart(HttpContext http, BootGuard boot, IProcessRestarter restarter)
    {
        boot.Reset();
        restarter.RestartAfter(http);
        return Results.Accepted();
    }
}
