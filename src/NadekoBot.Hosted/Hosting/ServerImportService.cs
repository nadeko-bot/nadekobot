using System.Buffers;
using System.Collections.Frozen;
using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using NadekoBot.Db.ServerData;
using SQLitePCL;

namespace NadekoBot.Hosted;

// Error is an ApiErrors code, sent with Status.
public readonly record struct ServerImportResult(string? Error, int Status, ServerImportDto? Import)
{
    public static ServerImportResult From(BackupResult backup)
        => new(backup.Error, backup.Status, null);

    public static ServerImportResult Invalid(string error)
        => new(error, StatusCodes.Status400BadRequest, null);

    public static ServerImportResult Conflict(string error)
        => new(error, StatusCodes.Status409Conflict, null);

    public static readonly ServerImportResult NoSpace
        = new(ApiErrors.NO_SPACE, StatusCodes.Status507InsufficientStorage, null);

    public static readonly ServerImportResult Failed
        = new(ApiErrors.IMPORT_FAILED, StatusCodes.Status500InternalServerError, null);
}

public sealed class ServerImportService(
    HostPaths paths,
    RestoreTarget target,
    BackupService backups)
{
    private const int EXPECTED_ENTRIES = 2;
    private const int BUFFER_SIZE = 81920;
    private const string ZIP_EXTENSION = ".zip";
    private const string DB_EXTENSION = ".db";

    // Runs only while this instance holds the backup lock. On success the lock stays taken until the process exits,
    // so nothing can change the data between the merge and the restart which reloads it.
    public async Task<ServerImportResult> ImportAsync(
        ulong guildId,
        Stream body,
        long? contentLength,
        DbService db,
        CancellationToken ct)
    {
        if (target.DbPath is null)
            return ServerImportResult.Conflict(ApiErrors.BACKUP_UNSUPPORTED);

        if (backups.TryBeginServerImport() is { } busy)
            return ServerImportResult.From(busy);

        var done = false;
        var id = Guid.NewGuid().ToString("N");
        var zipPath = Path.Combine(paths.ServerImportDir, id + ZIP_EXTENSION);
        var dbPath = Path.Combine(paths.ServerImportDir, id + DB_EXTENSION);
        try
        {
            DeleteStagingInternal();
            Directory.CreateDirectory(paths.ServerImportDir);

            if (!BackupArchive.HasFreeSpace(paths.ServerImportDir, contentLength ?? 0))
                return ServerImportResult.NoSpace;

            await using (var temp = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             BUFFER_SIZE, useAsync: true))
            {
                try
                {
                    await body.CopyToAsync(temp, ct);
                }
                catch (BadHttpRequestException ex)
                {
                    return ServerImportResult.From(BackupResult.FromBadRequest(ex.StatusCode));
                }
            }

            var (error, manifest) = await Task.Run(() => ExtractInternal(zipPath, dbPath, guildId, ct), ct);
            if (error is not null)
                return error.Value;

            var result = await Task.Run(() => PrepareAndMergeInternalAsync(db, dbPath, guildId, manifest!), ct);
            done = result.Error is null;
            return result;
        }
        catch (Exception ex) when (ex is IOException or SqliteException or InvalidDataException && !ct.IsCancellationRequested)
        {
            Log.Error(ex, "Importing the server data of {GuildId} failed", guildId);
            return BackupArchive.HasFreeSpace(paths.ServerImportDir, 0) ? ServerImportResult.Failed : ServerImportResult.NoSpace;
        }
        finally
        {
            DeleteStagingInternal();
            if (!done)
                backups.EndServerImport();
        }
    }

    private async Task<ServerImportResult> PrepareAndMergeInternalAsync(
        DbService db,
        string dbPath,
        ulong guildId,
        ServerExportManifest manifest)
    {
        var livePath = target.DbPath!;
        FrozenDictionary<string, string[]>? columns;
        using (var staged = ServerDataMerger.OpenStaged(dbPath))
        {
            if (PrepareStagedInternal(staged, out var schema) is { } bad)
                return ServerImportResult.Invalid(bad);

            await using (var ctx = new NadekoContext(staged))
            {
                if (string.CompareOrdinal(schema, DbMigrator.GetLatestKnown(ctx)) > 0)
                    return ServerImportResult.Conflict(ApiErrors.EXPORT_TOO_NEW);

                try
                {
                    await DbMigrator.ApplySqlMigrationsAsync(ctx, schema);
                }
                catch (SqliteException ex)
                {
                    Log.Warning(ex, "A migration failed on the server export of {GuildId}", guildId);
                    return ServerImportResult.Conflict(ApiErrors.SCHEMA_MISMATCH);
                }
            }

            using var live = ServerDataMerger.OpenLive(livePath, SqliteOpenMode.ReadOnly);
            columns = ServerDataMerger.MatchColumns(live, staged);
        }

        if (columns is null)
            return ServerImportResult.Conflict(ApiErrors.SCHEMA_MISMATCH);

        // The imported rows can grow the live database by about the size of the export.
        if (!BackupArchive.HasFreeSpace(paths.ServerImportDir, new FileInfo(dbPath).Length))
            return ServerImportResult.NoSpace;

        var backup = await backups.CreateLockedAsync(db);
        if (backup.Backup is not { } backupDto)
            return ServerImportResult.From(backup);

        long rows;
        try
        {
            rows = ServerDataMerger.Merge(livePath, dbPath, guildId, manifest.XpIncluded, columns);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_CONSTRAINT)
        {
            // The export breaks a rule of the schema, such as two rows with one unique key.
            Log.Warning(ex, "The server export of {GuildId} has rows which the database refuses", guildId);
            return ServerImportResult.Invalid(ApiErrors.BAD_EXPORT);
        }

        Log.Information("Imported {Rows} rows of server data for {GuildId}", rows, guildId);
        return new(null, StatusCodes.Status202Accepted, new(backupDto, manifest.XpIncluded, rows));
    }

    // A broken file can fail anywhere in the schema checks; each such failure is a bad export.
    private static string? PrepareStagedInternal(SqliteConnection staged, out string schema)
    {
        schema = string.Empty;
        try
        {
            if (ServerDataMerger.Prepare(staged) is { } bad)
                return bad;

            schema = ServerDataMerger.ReadSchema(staged) ?? string.Empty;
            return schema.Length == 0 ? ApiErrors.BAD_EXPORT : null;
        }
        catch (SqliteException ex)
        {
            Log.Warning(ex, "A server export failed the checks");
            return ApiErrors.BAD_EXPORT;
        }
    }

    private (ServerImportResult? Error, ServerExportManifest? Manifest) ExtractInternal(
        string zipPath,
        string dbPath,
        ulong guildId,
        CancellationToken ct)
    {
        using var zip = File.OpenRead(zipPath);
        try
        {
            if (BackupArchive.ReadEntryCount(zip) != EXPECTED_ENTRIES)
                return (ServerImportResult.Invalid(ApiErrors.BAD_EXPORT), null);

            zip.Position = 0;
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count != EXPECTED_ENTRIES
                || archive.GetEntry(ServerDataExporter.MANIFEST_ENTRY) is not { } manifestEntry
                || archive.GetEntry(ServerDataExporter.DB_ENTRY) is not { } dbEntry)
                return (ServerImportResult.Invalid(ApiErrors.BAD_EXPORT), null);

            var maxDbBytes = backups.ImportLimits.MaxUncompressedBytes;
            if (manifestEntry.Length > ServerExportManifest.MAX_BYTES || dbEntry.Length > maxDbBytes)
                return (ServerImportResult.Invalid(ApiErrors.ARCHIVE_TOO_LARGE), null);

            if (ReadManifestInternal(manifestEntry) is not { } manifest)
                return (ServerImportResult.Invalid(ApiErrors.BAD_EXPORT), null);

            if (manifest.GuildId != guildId)
                return (ServerImportResult.Invalid(ApiErrors.GUILD_MISMATCH), null);

            if (!BackupArchive.HasSqliteHeader(dbEntry))
                return (ServerImportResult.Invalid(ApiErrors.NOT_SQLITE), null);

            // Twice: the extracted file, and the indexes and migrations which grow it.
            if (!BackupArchive.HasFreeSpace(paths.ServerImportDir, dbEntry.Length * 2))
                return (ServerImportResult.NoSpace, null);

            return CopyLimitedInternal(dbEntry, dbPath, dbEntry.Length, ct)
                ? (null, manifest)
                : (ServerImportResult.Invalid(ApiErrors.ARCHIVE_TOO_LARGE), null);
        }
        catch (InvalidDataException)
        {
            return (ServerImportResult.Invalid(ApiErrors.NOT_A_ZIP), null);
        }
    }

    private static ServerExportManifest? ReadManifestInternal(ZipArchiveEntry entry)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ServerExportManifest.MAX_BYTES + 1);
        try
        {
            using var stream = entry.Open();
            var read = stream.ReadAtLeast(buffer.AsSpan(0, ServerExportManifest.MAX_BYTES + 1),
                ServerExportManifest.MAX_BYTES + 1, throwOnEndOfStream: false);
            return read > ServerExportManifest.MAX_BYTES
                ? null
                : ServerExportManifest.TryParse(buffer.AsSpan(0, read));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // The declared size comes from the archive, so the copy stops at it instead of trusting the stream.
    private static bool CopyLimitedInternal(ZipArchiveEntry entry, string destPath, long maxBytes, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
        try
        {
            using var source = entry.Open();
            using var dest = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BUFFER_SIZE);
            long total = 0;
            int read;
            while ((read = source.Read(buffer, 0, BUFFER_SIZE)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                total += read;
                if (total > maxBytes)
                    return false;

                dest.Write(buffer, 0, read);
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Runs only under the lock, so each file in the folder is from this or a crashed import.
    private void DeleteStagingInternal()
    {
        try
        {
            if (Directory.Exists(paths.ServerImportDir))
                Directory.Delete(paths.ServerImportDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Failed to delete the server import folder {Dir}", paths.ServerImportDir);
        }
    }
}
