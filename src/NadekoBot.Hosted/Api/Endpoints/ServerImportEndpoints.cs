using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class ServerImportEndpoints
{
    private const string ZIP_CONTENT_TYPE = "application/zip";

    public static RouteGroupBuilder MapServerImportEndpoint(this RouteGroupBuilder ownerGuild)
    {
        ownerGuild.MapPost("/import", ImportServer);
        return ownerGuild;
    }

    private static async Task<IResult> ImportServer(
        GuildRef guildRef,
        HttpContext http,
        ApiSettings settings,
        DbService db,
        ServerImportService imports,
        IProcessRestarter restarter)
    {
        if (!BackupEndpoints.HasMediaType(http.Request.ContentType, ZIP_CONTENT_TYPE))
            return ApiErrors.Create(ApiErrors.UNSUPPORTED_MEDIA_TYPE, StatusCodes.Status415UnsupportedMediaType);

        var limit = settings.MaxImportBytes;
        if (http.Request.ContentLength > limit)
            return ApiErrors.Create(ApiErrors.TOO_LARGE, StatusCodes.Status413PayloadTooLarge);

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            sizeFeature.MaxRequestBodySize = limit;

        var result = await imports.ImportAsync(guildRef.Id, http.Request.Body, http.Request.ContentLength, db,
            http.RequestAborted);
        if (result.Import is not { } import)
            return ApiErrors.Create(result.Error ?? ApiErrors.INTERNAL_ERROR, result.Status);

        // The running bot keeps the old settings in memory, so it starts again to load the imported ones.
        restarter.RestartAfter(http);
        return Results.Json(import, ApiJsonContext.Default.ServerImportDto, statusCode: StatusCodes.Status202Accepted);
    }
}
