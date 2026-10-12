using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class BackupEndpoints
{
    private const string ZIP_CONTENT_TYPE = "application/zip";
    private const int DOWNLOAD_BUFFER_SIZE = 81920;

    public static RouteGroupBuilder MapBackupCreateEndpoint(this RouteGroupBuilder owner)
    {
        owner.MapPost("/backup", CreateBackup);
        return owner;
    }

    public static RouteGroupBuilder MapBackupFileEndpoints(this RouteGroupBuilder files)
    {
        files.MapGet("/backups", ListBackups);
        files.MapGet("/backups/{fileName}", DownloadBackup);
        files.MapDelete("/backups/{fileName}", DeleteBackup);
        return files;
    }

    public static RouteGroupBuilder MapBackupWriteEndpoints(this RouteGroupBuilder files)
    {
        files.MapPost("/backups/import", ImportBackup);
        files.MapPost("/backups/{fileName}/restore", RestoreBackup);
        return files;
    }

    private static async Task<IResult> CreateBackup(DbService db, BackupService backups)
        => ToResultInternal(await backups.CreateAsync(db));

    private static async Task<IResult> ImportBackup(HttpContext http, ApiSettings settings, BackupService backups)
    {
        if (!HasMediaType(http.Request.ContentType, ZIP_CONTENT_TYPE))
            return ApiErrors.Create(ApiErrors.UNSUPPORTED_MEDIA_TYPE, StatusCodes.Status415UnsupportedMediaType);

        var limit = settings.MaxImportBytes;
        if (http.Request.ContentLength > limit)
            return ApiErrors.Create(ApiErrors.TOO_LARGE, StatusCodes.Status413PayloadTooLarge);

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            sizeFeature.MaxRequestBodySize = limit;

        return ToResultInternal(await backups.ImportAsync(http.Request.Body, http.Request.ContentLength, http.RequestAborted));
    }

    public static bool HasMediaType(string? contentType, string expected)
    {
        var mediaType = contentType.AsSpan();
        if (mediaType.IndexOf(';') is var end and >= 0)
            mediaType = mediaType[..end];

        return mediaType.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IResult> RestoreBackup(
        string fileName,
        HttpContext http,
        BackupService backups,
        IProcessRestarter restarter)
    {
        var result = await backups.RequestRestoreAsync(fileName);
        if (result.Error is not null)
            return ToErrorInternal(result);

        restarter.RestartAfter(http);
        return Results.Accepted();
    }

    private static IResult ListBackups(BackupService backups)
        => Results.Json(backups.List(), ApiJsonContext.Default.ListBackupDto);

    private static IResult DownloadBackup(string fileName, BackupService backups)
    {
        if (!backups.TryGetPath(fileName, out var path))
            return ApiErrors.NotFound;

        // A delete may run at the same time; once the file is open, a delete no longer breaks the transfer.
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                DOWNLOAD_BUFFER_SIZE, useAsync: true);
            return Results.Stream(stream, ZIP_CONTENT_TYPE, fileName);
        }
        catch (FileNotFoundException)
        {
            return ApiErrors.NotFound;
        }
    }

    private static IResult DeleteBackup(string fileName, BackupService backups)
        => backups.Delete(fileName) is { Error: not null } failed
            ? ToErrorInternal(failed)
            : Results.NoContent();

    private static IResult ToResultInternal(BackupResult result)
        => result.Backup is { } backup
            ? Results.Json(backup, ApiJsonContext.Default.BackupDto)
            : ToErrorInternal(result);

    private static IResult ToErrorInternal(BackupResult result)
        => ApiErrors.Create(result.Error ?? ApiErrors.INTERNAL_ERROR, result.Status);
}
