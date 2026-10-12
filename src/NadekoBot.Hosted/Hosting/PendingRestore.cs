using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace NadekoBot.Hosted;

// Runs before the bot opens the database; each step is safe to repeat, and the marker records the progress.
public static class PendingRestore
{
    public const int MAX_RESTORE_ATTEMPTS = 3;

    private const string CREATED_LIST_FILE = ".created";
    private const string STAGED_DB = ".db";
    private const string STAGED_DATA = "data";
    private const string PRE_RESTORE_SUFFIX = ".pre-restore";
    private const string QUICK_CHECK_OK = "ok";
    private const UnixFileMode OWNER_RW = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly string[] _sqliteSidecars = ["-wal", "-shm"];

    // False when the set-aside database could not be put back; a start then would create an empty database.
    public static bool Apply(HostPaths paths, string? dbPath)
    {
        if (!File.Exists(paths.RestoreMarker))
            return true;

        try
        {
            ApplyInternal(paths, dbPath);
            return true;
        }
        catch (Exception ex)
        {
            if (dbPath is not null && !File.Exists(dbPath) && File.Exists(dbPath + PRE_RESTORE_SUFFIX))
            {
                Log.Fatal(ex, "The pending restore failed and {Previous} could not be put back. Move it to {Db} by hand",
                    dbPath + PRE_RESTORE_SUFFIX,
                    dbPath);
                return false;
            }

            // Keeping the marker would fail every start the same way.
            Log.Error(ex, "The pending restore failed. Starting on the current data");
            try
            {
                File.Delete(paths.RestoreMarker);
                return true;
            }
            catch (Exception deleteEx) when (deleteEx is IOException or UnauthorizedAccessException)
            {
                Log.Fatal(deleteEx, "Could not delete {Marker}. Delete it by hand", paths.RestoreMarker);
                return false;
            }
        }
    }

    private static void ApplyInternal(HostPaths paths, string? dbPath)
    {
        var marker = RestoreMarker.Parse(File.ReadAllText(paths.RestoreMarker));
        var zipPath = Path.Combine(paths.BackupDir, marker.FileName);

        if (marker.Attempts >= MAX_RESTORE_ATTEMPTS)
        {
            Log.Error("Discarding the pending restore of {FileName}: it failed {Attempts} times",
                marker.FileName,
                marker.Attempts);
            DiscardInternal(paths, dbPath, marker);
            return;
        }

        if (dbPath is null
            || !BackupArchive.IsValidBackupFileName(marker.FileName)
            || !File.Exists(zipPath)
            || BackupArchive.ValidateFile(zipPath, BackupLimits.Restore, out var uncompressed) is not null)
        {
            Log.Error("Discarding the pending restore of {FileName}: the archive or the database is not restorable",
                marker.FileName);
            DiscardInternal(paths, dbPath, marker);
            return;
        }

        if (!BackupArchive.HasFreeSpace(paths.DataDir, uncompressed))
        {
            Log.Error("Discarding the pending restore of {FileName}: not enough free disk space", marker.FileName);
            DiscardInternal(paths, dbPath, marker);
            return;
        }

        marker = marker with { Attempts = marker.Attempts + 1 };
        SecretFile.WriteAllText(paths.RestoreMarker, marker.ToString());

        try
        {
            RestoreInternal(paths, dbPath, zipPath, marker);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restoring {FileName} failed. Keeping the current database", marker.FileName);
            var current = File.Exists(paths.RestoreMarker)
                ? RestoreMarker.Parse(File.ReadAllText(paths.RestoreMarker))
                : marker;
            DiscardInternal(paths, dbPath, current);
        }
    }

    private static void RestoreInternal(HostPaths paths, string dbPath, string zipPath, RestoreMarker marker)
    {
        Log.Information("Restoring {FileName}", marker.FileName);

        var staging = paths.RestoreStaging;
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);

        Directory.CreateDirectory(staging);
        var staged = StageInternal(zipPath, staging);
        CheckIntegrityInternal(Path.Combine(staging, STAGED_DB));

        // Once the live data is aside, a retry must not move the partly restored data over it.
        if (marker.SetAside)
        {
            DeleteDbInternal(dbPath);
        }
        else
        {
            CopyFilesAsideInternal(paths, staged);
            SetAsideInternal(dbPath);
            SecretFile.WriteAllText(paths.RestoreMarker, (marker with { SetAside = true }).ToString());
        }

        File.Move(Path.Combine(staging, STAGED_DB), dbPath);

        foreach (var relative in staged)
        {
            var dest = Path.Combine(paths.DataDir, relative);
            File.Move(Path.Combine(staging, STAGED_DATA, relative), dest, overwrite: true);
        }

        Directory.Delete(staging, recursive: true);
        File.Delete(paths.RestoreMarker);

        Log.Information("Restored {FileName}. The previous data is kept as {Previous} and {PreviousFiles}",
            marker.FileName,
            dbPath + PRE_RESTORE_SUFFIX,
            paths.PreRestoreFiles);
    }

    private static List<string> StageInternal(string zipPath, string staging)
    {
        List<string> staged = [];
        using var archive = ZipFile.OpenRead(zipPath);

        var db = BackupArchive.FindDbEntry(archive) ?? throw new InvalidDataException($"{zipPath} has no database");
        ExtractOwnerOnlyInternal(db, Path.Combine(staging, STAGED_DB));

        var dataDir = Path.Combine(staging, STAGED_DATA);
        Directory.CreateDirectory(dataDir);
        foreach (var entry in archive.Entries)
        {
            if (BackupArchive.IsDbEntry(entry.FullName) || BackupArchive.RestoreEntryPath(entry.FullName) is not { } relative)
                continue;

            ExtractOwnerOnlyInternal(entry, Path.Combine(dataDir, relative));
            staged.Add(relative);
        }

        return staged;
    }

    // ExtractToFile keeps the mode bits from the zip, which a crafted archive can set to world-writable or setuid.
    private static void ExtractOwnerOnlyInternal(ZipArchiveEntry entry, string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = OWNER_RW;

        using var source = entry.Open();
        using var dest = new FileStream(path, options);
        source.CopyTo(dest);
        dest.Flush(flushToDisk: true);
    }

    private static void CheckIntegrityInternal(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check";
        if (cmd.ExecuteScalar() is not QUICK_CHECK_OK)
            throw new InvalidDataException("The database in the archive is damaged");
    }

    private static void DiscardInternal(HostPaths paths, string? dbPath, RestoreMarker marker)
    {
        if (marker.SetAside)
            PutBackFilesInternal(paths);

        // Without the set-aside record, a missing live database still means an attempt moved it aside.
        if (dbPath is not null && (marker.SetAside || !File.Exists(dbPath)))
            PutBackInternal(dbPath);

        if (Directory.Exists(paths.RestoreStaging))
            Directory.Delete(paths.RestoreStaging, recursive: true);

        File.Delete(paths.RestoreMarker);
    }

    // Copies, not moves: an attempt which stops before it records the set-aside leaves the live files untouched.
    private static void CopyFilesAsideInternal(HostPaths paths, List<string> staged)
    {
        var aside = paths.PreRestoreFiles;
        if (Directory.Exists(aside))
            Directory.Delete(aside, recursive: true);

        Directory.CreateDirectory(aside);
        List<string> created = [];
        foreach (var relative in staged)
        {
            var live = Path.Combine(paths.DataDir, relative);
            if (File.Exists(live))
                File.Copy(live, Path.Combine(aside, relative));
            else
                created.Add(relative);
        }

        File.WriteAllLines(Path.Combine(aside, CREATED_LIST_FILE), created);
    }

    private static void PutBackFilesInternal(HostPaths paths)
    {
        var aside = paths.PreRestoreFiles;
        var createdList = Path.Combine(aside, CREATED_LIST_FILE);
        if (!File.Exists(createdList))
            return;

        foreach (var relative in File.ReadLines(createdList))
        {
            if (BackupArchive.IsRestoreIncluded(relative))
                TryDeleteInternal(Path.Combine(paths.DataDir, relative));
        }

        foreach (var file in Directory.EnumerateFiles(aside))
        {
            var name = Path.GetFileName(file);
            if (BackupArchive.IsRestoreIncluded(name))
                File.Copy(file, Path.Combine(paths.DataDir, name), overwrite: true);
        }
    }

    // Checkpointed first, so one move of the file alone sets it aside; separate moves could lose the -wal in a crash.
    private static void SetAsideInternal(string dbPath)
    {
        // An earlier attempt moved the checkpointed original aside before recording it; a -wal left here is not its.
        if (!File.Exists(dbPath))
        {
            DeleteDbInternal(dbPath);
            return;
        }

        CheckpointInternal(dbPath);

        var aside = dbPath + PRE_RESTORE_SUFFIX;
        foreach (var sidecar in _sqliteSidecars)
        {
            // An earlier restore's -wal next to this database would be applied to it.
            TryDeleteInternal(aside + sidecar);
            TryDeleteInternal(dbPath + sidecar);
        }

        File.Move(dbPath, aside, overwrite: true);
    }

    // The bot has not started, so this is the only connection; TRUNCATE fails rather than leave frames in the -wal.
    private static void CheckpointInternal(string dbPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();

        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) != 0)
            throw new IOException($"Could not checkpoint {dbPath}");
    }

    private static void PutBackInternal(string dbPath)
    {
        DeleteDbInternal(dbPath);
        MoveIfExistsInternal(dbPath + PRE_RESTORE_SUFFIX, dbPath);
    }

    private static void DeleteDbInternal(string dbPath)
    {
        TryDeleteInternal(dbPath);
        foreach (var sidecar in _sqliteSidecars)
            TryDeleteInternal(dbPath + sidecar);
    }

    private static void MoveIfExistsInternal(string from, string to)
    {
        if (File.Exists(from))
            File.Move(from, to, overwrite: true);
    }

    private static void TryDeleteInternal(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

// data/.restore-pending: the archive name, "set-aside" once the live database was moved aside, and the attempt count.
public readonly record struct RestoreMarker(string FileName, bool SetAside, int Attempts)
{
    private const string PENDING_STATE = "pending";
    private const string SET_ASIDE_STATE = "set-aside";

    public static RestoreMarker Request(string fileName)
        => new(fileName, false, 0);

    public static RestoreMarker Parse(ReadOnlySpan<char> text)
    {
        Span<Range> lines = stackalloc Range[4];
        var count = text.Split(lines, '\n');

        var fileName = count > 0 ? text[lines[0]].Trim().ToString() : string.Empty;
        var setAside = count > 1 && text[lines[1]].Trim().SequenceEqual(SET_ASIDE_STATE);
        var attempts = count > 2 && int.TryParse(text[lines[2]].Trim(), out var n) ? n : 0;
        return new(fileName, setAside, attempts);
    }

    public override string ToString()
        => $"{FileName}\n{(SetAside ? SET_ASIDE_STATE : PENDING_STATE)}\n{Attempts}";
}
