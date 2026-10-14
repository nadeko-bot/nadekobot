using System.Globalization;
using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Hosted;

// Error is an ApiErrors code, sent with Status.
public readonly record struct BackupResult(string? Error, int Status, BackupDto? Backup)
{
    public static readonly BackupResult Done = new(null, StatusCodes.Status200OK, null);
    public static readonly BackupResult NotFound = new(ApiErrors.NOT_FOUND, StatusCodes.Status404NotFound, null);
    public static readonly BackupResult NoSpace = new(ApiErrors.NO_SPACE, StatusCodes.Status507InsufficientStorage, null);
    public static readonly BackupResult Failed = new(ApiErrors.BACKUP_FAILED, StatusCodes.Status500InternalServerError, null);

    public static BackupResult Ok(BackupDto backup)
        => new(null, StatusCodes.Status200OK, backup);

    public static BackupResult Conflict(string error)
        => new(error, StatusCodes.Status409Conflict, null);

    // Kestrel's reply to a body it could not read: 413 above the size limit, 400 for a broken upload.
    public static BackupResult FromBadRequest(int status)
        => new(ApiErrors.CodeFor(status), status, null);

    public static BackupResult Invalid(string error)
        => new(error, StatusCodes.Status400BadRequest, null);
}

public sealed class BackupService(
    HostPaths paths,
    ApiSettings settings,
    RestoreTarget target,
    BootGuard boot,
    TimeProvider time)
{
    private const int MAX_NAME_ATTEMPTS = 5;
    private const string TEMP_FILE_PREFIX = ".";
    private const string SNAPSHOT_TEMP_PREFIX = TEMP_FILE_PREFIX + "snapshot-";
    private const string BACKUP_TEMP_PREFIX = TEMP_FILE_PREFIX + "backup-";
    private const string IMPORT_TEMP_PREFIX = TEMP_FILE_PREFIX + "import-";
    private const string ANY_FILE_PATTERN = "*";
    private const string SNAPSHOT_EXTENSION = ".db";
    private const string WAL_SUFFIX = "-wal";

    // Only these, so dot-files the owner keeps in the folder (.gitkeep, rsync partials) survive.
    private static readonly string[] _tempPrefixes = [SNAPSHOT_TEMP_PREFIX, BACKUP_TEMP_PREFIX, IMPORT_TEMP_PREFIX];

    private const int BUFFER_SIZE = 81920;

    private const int IDLE = 0;
    private const int CREATING = 1;
    private const int IMPORTING = 2;
    private const int RESTORING = 3;
    private const int DELETING = 4;
    private const int UPDATING = 5;
    private const int SERVER_IMPORTING = 6;

    private int _busy;

    public BackupLimits ImportLimits
        => BackupLimits.ForImport(settings.MaxImportBytes);

    public List<BackupDto> List()
    {
        if (!Directory.Exists(paths.BackupDir))
            return [];

        var backups = new List<BackupDto>();
        foreach (var file in new DirectoryInfo(paths.BackupDir).EnumerateFiles())
        {
            if (BackupArchive.IsValidBackupFileName(file.Name))
                backups.Add(ToDtoInternal(file));
        }

        backups.Sort(static (a, b) => string.CompareOrdinal(b.FileName, a.FileName));
        return backups;
    }

    public bool TryGetPath(string fileName, out string path)
    {
        path = Path.Combine(paths.BackupDir, fileName);
        return BackupArchive.IsValidBackupFileName(fileName) && File.Exists(path);
    }

    // Takes _busy so a restore cannot validate the archive and then lose it.
    public BackupResult Delete(string fileName)
    {
        if (!TryGetPath(fileName, out var path))
            return BackupResult.NotFound;

        if (TryBeginInternal(DELETING) is { } busy)
            return busy;

        try
        {
            File.Delete(path);
            return BackupResult.Done;
        }
        finally
        {
            Volatile.Write(ref _busy, IDLE);
        }
    }

    public async Task<BackupResult> CreateAsync(DbService db)
    {
        if (target.DbPath is null)
            return BackupResult.Conflict(ApiErrors.BACKUP_UNSUPPORTED);

        if (TryBeginInternal(CREATING) is { } busy)
            return busy;

        try
        {
            return await CreateLockedAsync(db);
        }
        finally
        {
            Volatile.Write(ref _busy, IDLE);
        }
    }

    // For a caller which already holds the lock, such as a server import.
    public async Task<BackupResult> CreateLockedAsync(DbService db)
    {
        if (target.DbPath is null)
            return BackupResult.Conflict(ApiErrors.BACKUP_UNSUPPORTED);

        var id = Guid.NewGuid();
        var tempDbPath = Path.Combine(paths.BackupDir, $"{SNAPSHOT_TEMP_PREFIX}{id:N}{SNAPSHOT_EXTENSION}");
        var tempZipPath = Path.Combine(paths.BackupDir, $"{BACKUP_TEMP_PREFIX}{id:N}{BackupArchive.BACKUP_EXTENSION}");

        try
        {
            Directory.CreateDirectory(paths.BackupDir);
            DeleteStaleTempFilesInternal();

            // The snapshot and the archive each take up to the size of the live database, with its WAL.
            var dbSize = FileSizeInternal(target.DbPath) + FileSizeInternal(target.DbPath + WAL_SUFFIX);
            if (!BackupArchive.HasFreeSpace(paths.BackupDir, dbSize * 2))
                return BackupResult.NoSpace;

            // Microsoft.Data.Sqlite runs commands synchronously, and VACUUM INTO takes as long as the database is big.
            await Task.Run(() => SnapshotDatabaseInternalAsync(db, tempDbPath));
            await WriteArchiveInternalAsync(tempDbPath, tempZipPath);
            return PublishInternal(tempZipPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create a data backup");
            // SQLite reports a full disk as its own error, not as an IOException.
            return BackupArchive.HasFreeSpace(paths.BackupDir, 0) ? BackupResult.Failed : BackupResult.NoSpace;
        }
        finally
        {
            TryDeleteInternal(tempDbPath);
            TryDeleteInternal(tempZipPath);
        }
    }

    private static long FileSizeInternal(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? info.Length : 0;
    }

    public async Task<BackupResult> ImportAsync(Stream body, long? contentLength, CancellationToken ct)
    {
        if (TryBeginInternal(IMPORTING) is { } busy)
            return busy;

        var tempPath = Path.Combine(paths.BackupDir, $"{IMPORT_TEMP_PREFIX}{Guid.NewGuid():N}{BackupArchive.BACKUP_EXTENSION}");
        try
        {
            Directory.CreateDirectory(paths.BackupDir);
            DeleteStaleTempFilesInternal();

            // Without a length only the margin is checked; a full disk during the copy still answers no_space.
            if (!BackupArchive.HasFreeSpace(paths.BackupDir, contentLength ?? 0))
                return BackupResult.NoSpace;

            await using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             BUFFER_SIZE, useAsync: true))
            {
                try
                {
                    await body.CopyToAsync(temp, ct);
                }
                catch (BadHttpRequestException ex)
                {
                    return BackupResult.FromBadRequest(ex.StatusCode);
                }
            }

            var (invalid, _) = await BackupArchive.ValidateFileAsync(tempPath, ImportLimits);
            if (invalid is not null)
                return BackupResult.Invalid(invalid);

            return PublishInternal(tempPath);
        }
        catch (IOException ex) when (!ct.IsCancellationRequested)
        {
            // The disk can fill between the free-space check and the write.
            Log.Error(ex, "Importing a backup failed");
            return BackupArchive.HasFreeSpace(paths.BackupDir, 0) ? BackupResult.Failed : BackupResult.NoSpace;
        }
        finally
        {
            TryDeleteInternal(tempPath);
            Volatile.Write(ref _busy, IDLE);
        }
    }

    public async Task<BackupResult> RequestRestoreAsync(string fileName)
    {
        if (!TryGetPath(fileName, out var path))
            return BackupResult.NotFound;

        if (target.DbPath is null)
            return BackupResult.Conflict(ApiErrors.RESTORE_UNSUPPORTED);

        if (TryBeginInternal(RESTORING) is { } busy)
            return busy;

        // A delete may have finished between the first check and taking _busy.
        if (!File.Exists(path))
            return ReleaseInternal(BackupResult.NotFound);

        try
        {
            var (invalid, uncompressed) = await BackupArchive.ValidateFileAsync(path, BackupLimits.Restore);
            if (invalid is not null)
                return ReleaseInternal(BackupResult.Invalid(invalid));

            if (!BackupArchive.HasFreeSpace(paths.DataDir, uncompressed))
                return ReleaseInternal(BackupResult.NoSpace);

            SecretFile.WriteAllText(paths.RestoreMarker, RestoreMarker.Request(fileName).ToString());
            boot.Reset();
        }
        catch
        {
            Volatile.Write(ref _busy, IDLE);
            throw;
        }

        // _busy stays RESTORING: the process exits next, and nothing may change the backups before it does.
        return BackupResult.Done;
    }

    public static IEnumerable<(string AbsolutePath, string EntryName)> GetBackupCandidates(string dataDir)
    {
        if (!Directory.Exists(dataDir))
            yield break;

        // Only root files and medusae/ can be included, so the large asset folders are never walked.
        foreach (var path in Directory.EnumerateFiles(dataDir))
        {
            var name = Path.GetFileName(path);
            if (BackupArchive.IsBackupIncluded(name))
                yield return (path, BackupArchive.DATA_PREFIX + name);
        }

        var medusae = Path.Combine(dataDir, BackupArchive.MEDUSAE_DIR);
        if (!Directory.Exists(medusae))
            yield break;

        foreach (var path in Directory.EnumerateFiles(medusae, ANY_FILE_PATTERN, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(dataDir, path).Replace(Path.DirectorySeparatorChar, '/');
            yield return (path, BackupArchive.DATA_PREFIX + relative);
        }
    }

    private BackupResult? TryBeginInternal(int operation)
        => Interlocked.CompareExchange(ref _busy, operation, IDLE) switch
        {
            IDLE => null,
            CREATING => BackupResult.Conflict(ApiErrors.BACKUP_IN_PROGRESS),
            IMPORTING => BackupResult.Conflict(ApiErrors.IMPORT_IN_PROGRESS),
            RESTORING => BackupResult.Conflict(ApiErrors.RESTORE_IN_PROGRESS),
            UPDATING => BackupResult.Conflict(ApiErrors.UPDATE_IN_PROGRESS),
            SERVER_IMPORTING => BackupResult.Conflict(ApiErrors.SERVER_IMPORT_IN_PROGRESS),
            _ => BackupResult.Conflict(ApiErrors.DELETE_IN_PROGRESS)
        };

    // An update shares the lock, so a restore cannot be pending when the supervisor swaps the binaries.
    public BackupResult? TryBeginUpdate()
        => TryBeginInternal(UPDATING);

    public void EndUpdate()
        => Interlocked.CompareExchange(ref _busy, IDLE, UPDATING);

    // A server import makes a backup and writes the live database, so no other backup work may run meanwhile.
    public BackupResult? TryBeginServerImport()
        => TryBeginInternal(SERVER_IMPORTING);

    public void EndServerImport()
        => Interlocked.CompareExchange(ref _busy, IDLE, SERVER_IMPORTING);

    private BackupResult ReleaseInternal(BackupResult result)
    {
        Volatile.Write(ref _busy, IDLE);
        return result;
    }

    private async Task WriteArchiveInternalAsync(string snapshotPath, string zipPath)
    {
        await using var zipStream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            BUFFER_SIZE, useAsync: true);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        await AddEntryInternalAsync(archive, snapshotPath, BackupArchive.DB_ENTRY_NAME, CompressionLevel.Fastest);
        foreach (var (absolutePath, entryName) in GetBackupCandidates(paths.DataDir))
            await AddEntryInternalAsync(archive, absolutePath, entryName, CompressionLevel.Optimal);
    }

    private static async Task AddEntryInternalAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CompressionLevel level)
    {
        var entry = archive.CreateEntry(entryName, level);
        entry.LastWriteTime = File.GetLastWriteTimeUtc(sourcePath);

        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            BUFFER_SIZE, useAsync: true);
        await using var dest = entry.Open();
        await source.CopyToAsync(dest);
    }

    // A move without overwrite fails when the name exists, so two writers can never pick the same archive.
    private BackupResult PublishInternal(string tempPath)
    {
        var now = time.GetUtcNow().UtcDateTime;
        for (var attempt = 0; attempt < MAX_NAME_ATTEMPTS; attempt++)
        {
            var fileName = BackupArchive.BACKUP_PREFIX
                           + now.AddSeconds(attempt).ToString(BackupArchive.NAME_TIME_FORMAT, CultureInfo.InvariantCulture)
                           + BackupArchive.BACKUP_EXTENSION;
            var dest = Path.Combine(paths.BackupDir, fileName);
            try
            {
                File.Move(tempPath, dest, overwrite: false);
            }
            catch (IOException) when (File.Exists(dest))
            {
                continue;
            }

            return BackupResult.Ok(ToDtoInternal(new FileInfo(dest)));
        }

        return BackupResult.Conflict(ApiErrors.NAME_TAKEN);
    }

    private static BackupDto ToDtoInternal(FileInfo file)
        => new(file.Name, file.Length, BackupArchive.CreatedAt(file.Name));

    // Runs only while this instance holds _busy, so every temp file left in the folder is from a crashed run.
    private void DeleteStaleTempFilesInternal()
    {
        var dir = new DirectoryInfo(paths.BackupDir);
        foreach (var prefix in _tempPrefixes)
        {
            foreach (var file in dir.EnumerateFiles(prefix + ANY_FILE_PATTERN))
                TryDeleteInternal(file.FullName);
        }
    }

    private static async Task SnapshotDatabaseInternalAsync(DbService db, string destPath)
    {
        await using var ctx = db.GetDbContext();
        var conn = ctx.Database.GetDbConnection();

        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened)
            await conn.OpenAsync();

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM main INTO $path";
            var param = cmd.CreateParameter();
            param.ParameterName = "$path";
            param.Value = destPath;
            cmd.Parameters.Add(param);

            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            if (opened)
                await conn.CloseAsync();
        }
    }

    private static void TryDeleteInternal(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Failed to delete the temporary backup file {Path}", path);
        }
    }
}
