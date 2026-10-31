using System.Buffers;
using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace NadekoBot.Hosted;

public enum ImportFormat : byte
{
    Unknown,
    Zip,
    Gzip,
    Tar,
    SevenZip,
    Sqlite
}

// Error is an ApiErrors code; otherwise DbPath is the staged database and Settings the staged settings files.
public readonly record struct StagedImport(string? Error, string DbPath, List<(string Path, string EntryName)> Settings)
{
    public static StagedImport Fail(string error)
        => new(error, string.Empty, []);
}

// Turns an upload which is not a plain backup zip into the files a backup holds: the database with its -wal
// applied, and the settings files which a restore takes. Every other file in the upload is left out.
public static class BackupImport
{
    // Enough for the tar magic at offset 257 and the SQLite header.
    public const int SNIFF_BYTES = 512;

    public const string WAL_ENTRY_NAME = BackupArchive.DB_ENTRY_NAME + "-wal";

    private const int TAR_MAGIC_OFFSET = 257;
    private const int BUFFER_SIZE = 81920;
    private const string ROOT_DIR = "root";
    private const string DATA_DIR = "data";
    private const string CURRENT_DIR_PREFIX = "./";
    private const string QUICK_CHECK_OK = "ok";
    private const int SQLITE_FULL = 13;
    private const UnixFileMode OWNER_RW = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OWNER_RWX = OWNER_RW | UnixFileMode.UserExecute;

    private static ReadOnlySpan<byte> ZipMagic => [0x50, 0x4b, 0x03, 0x04];
    private static ReadOnlySpan<byte> EmptyZipMagic => [0x50, 0x4b, 0x05, 0x06];
    private static ReadOnlySpan<byte> GzipMagic => [0x1f, 0x8b];
    private static ReadOnlySpan<byte> SevenZipMagic => [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c];
    private static ReadOnlySpan<byte> TarMagic => "ustar"u8;

    public static ImportFormat Detect(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(ZipMagic) || head.StartsWith(EmptyZipMagic))
            return ImportFormat.Zip;
        if (head.StartsWith(GzipMagic))
            return ImportFormat.Gzip;
        if (head.StartsWith(SevenZipMagic))
            return ImportFormat.SevenZip;
        if (head.StartsWith(BackupArchive.SqliteHeader))
            return ImportFormat.Sqlite;
        if (IsTar(head))
            return ImportFormat.Tar;
        return ImportFormat.Unknown;
    }

    // POSIX ("ustar\0") and GNU ("ustar  \0") headers both start with these five bytes.
    private static bool IsTar(ReadOnlySpan<byte> head)
        => head.Length >= TAR_MAGIC_OFFSET + TarMagic.Length
           && head.Slice(TAR_MAGIC_OFFSET, TarMagic.Length).SequenceEqual(TarMagic);

    public static ImportFormat DetectFile(string path)
    {
        Span<byte> head = stackalloc byte[SNIFF_BYTES];
        using var fs = File.OpenRead(path);
        var read = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return Detect(head[..read]);
    }

    // A -wal holds the writes since the last checkpoint; a restore of the database alone would lose them.
    public static bool ZipHasWal(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > 0 && IsWalEntryName(entry.FullName))
                return true;
        }

        return false;
    }

    private static bool IsWalEntryName(string name)
        => name is WAL_ENTRY_NAME or BackupArchive.DATA_PREFIX + WAL_ENTRY_NAME;

    public static StagedImport Stage(string uploadPath, ImportFormat format, string stagingDir, BackupLimits limits)
    {
        CreateOwnerOnlyDirectoryInternal(Path.Combine(stagingDir, ROOT_DIR));
        CreateOwnerOnlyDirectoryInternal(Path.Combine(stagingDir, DATA_DIR));

        var error = format switch
        {
            ImportFormat.Zip => StageZipInternal(uploadPath, stagingDir, limits),
            ImportFormat.Tar => StageTarFileInternal(uploadPath, stagingDir, limits),
            ImportFormat.Gzip => StageGzipInternal(uploadPath, stagingDir, limits),
            ImportFormat.SevenZip => StageSevenZipInternal(uploadPath, stagingDir, limits),
            ImportFormat.Sqlite => StageDatabaseFileInternal(uploadPath, stagingDir),
            _ => ApiErrors.UNKNOWN_FORMAT
        };

        return error is null ? CollectInternal(stagingDir) : StagedImport.Fail(error);
    }

    private static void CreateOwnerOnlyDirectoryInternal(string path)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, OWNER_RWX);
    }

    // The folder of a kept entry decides which copy wins: the database at the root (as BackupArchive.FindDbEntry
    // picks it), the settings in data/ (the only folder a restore reads them from).
    private static StagedImport CollectInternal(string stagingDir)
    {
        var root = Path.Combine(stagingDir, ROOT_DIR);
        var data = Path.Combine(stagingDir, DATA_DIR);

        var dbDir = HasFileInternal(root, BackupArchive.DB_ENTRY_NAME) ? root
            : HasFileInternal(data, BackupArchive.DB_ENTRY_NAME) ? data
            : null;
        if (dbDir is null)
            return StagedImport.Fail(ApiErrors.MISSING_DB);

        // A -wal staged in the same folder sits under the name SQLite looks for, so FinishDatabase applies it.
        var dbPath = Path.Combine(dbDir, BackupArchive.DB_ENTRY_NAME);

        // A -wal beside the other copy holds writes of a database which the import does not take.
        var walDir = HasFileInternal(root, WAL_ENTRY_NAME) ? root
            : HasFileInternal(data, WAL_ENTRY_NAME) ? data
            : null;
        if (walDir is not null && !string.Equals(walDir, dbDir, StringComparison.Ordinal))
            return StagedImport.Fail(ApiErrors.DB_DAMAGED);

        List<(string Path, string EntryName)> settings = [];
        foreach (var path in Directory.EnumerateFiles(data))
        {
            var name = Path.GetFileName(path);
            if (BackupArchive.IsRestoreIncluded(name))
                settings.Add((path, BackupArchive.DATA_PREFIX + name));
        }

        foreach (var path in Directory.EnumerateFiles(root))
        {
            var name = Path.GetFileName(path);
            if (BackupArchive.IsRestoreIncluded(name) && !File.Exists(Path.Combine(data, name)))
                settings.Add((path, BackupArchive.DATA_PREFIX + name));
        }

        return new(null, dbPath, settings);
    }

    private static bool HasFileInternal(string dir, string name)
    {
        var info = new FileInfo(Path.Combine(dir, name));
        return info.Exists && info.Length > 0;
    }

    // Where a kept entry goes in the staging folder; null for an entry the import leaves out. Kept names are
    // fixed or pass IsSafeEntryName, so no entry can write outside the folder.
    public static string? StagingPath(string entryName)
    {
        // 7-Zip on Windows stores its own path separator.
        var name = (entryName.Contains('\\') ? entryName.Replace('\\', '/') : entryName).AsSpan();
        while (name.StartsWith(CURRENT_DIR_PREFIX, StringComparison.Ordinal))
            name = name[CURRENT_DIR_PREFIX.Length..];

        var dir = ROOT_DIR;
        if (name.StartsWith(BackupArchive.DATA_PREFIX, StringComparison.Ordinal))
        {
            dir = DATA_DIR;
            name = name[BackupArchive.DATA_PREFIX.Length..];
        }

        if (name.IsEmpty || name.Contains('/'))
            return null;

        var file = name.ToString();
        if (file is BackupArchive.DB_ENTRY_NAME or WAL_ENTRY_NAME)
            return Path.Combine(dir, file);

        return BackupArchive.IsRestoreIncluded(file) && BackupArchive.IsSafeEntryName(file)
            ? Path.Combine(dir, file)
            : null;
    }

    // The layout check already ran on the zip, so its entry names, sizes and settings files are known to be fine.
    private static string? StageZipInternal(string zipPath, string stagingDir, BackupLimits limits)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        // The folder parts the output zip as well, so the unpacked files need their size free twice.
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (StagingPath(entry.FullName) is null || entry.Length > limits.MaxUncompressedBytes - total)
                continue;

            total += entry.Length;
        }

        if (!HasSpaceForStagingInternal(stagingDir, total))
            return ApiErrors.NO_SPACE;

        var budget = limits.MaxUncompressedBytes;
        foreach (var entry in archive.Entries)
        {
            if (StagingPath(entry.FullName) is not { } relative)
                continue;

            using var source = entry.Open();
            if (!WriteStagedInternal(source, Path.Combine(stagingDir, relative), ref budget))
                return ApiErrors.ARCHIVE_TOO_LARGE;
        }

        return null;
    }

    // The staging folder holds the unpacked files and the output zip beside it.
    private static bool HasSpaceForStagingInternal(string stagingDir, long unpackedBytes)
        => BackupArchive.HasFreeSpace(stagingDir,
            unpackedBytes > long.MaxValue / 2 ? long.MaxValue : unpackedBytes * 2);

    private static string? StageTarFileInternal(string tarPath, string stagingDir, BackupLimits limits)
    {
        using var fs = File.OpenRead(tarPath);
        return StageTarInternal(fs, stagingDir, limits);
    }

    private static string? StageGzipInternal(string gzPath, string stagingDir, BackupLimits limits)
    {
        try
        {
            ImportFormat inner;
            using (var probe = new GZipStream(File.OpenRead(gzPath), CompressionMode.Decompress))
            {
                Span<byte> head = stackalloc byte[SNIFF_BYTES];
                var read = probe.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
                inner = Detect(head[..read]);
            }

            using var gz = new GZipStream(File.OpenRead(gzPath), CompressionMode.Decompress);
            if (inner is ImportFormat.Tar)
                return StageTarInternal(gz, stagingDir, limits);

            if (inner is not ImportFormat.Sqlite)
                return ApiErrors.UNKNOWN_FORMAT;

            // A database is about twice the size of its gzip.
            var gzLength = new FileInfo(gzPath).Length;
            if (!HasSpaceForStagingInternal(stagingDir, gzLength * 2))
                return ApiErrors.NO_SPACE;

            var budget = limits.MaxUncompressedBytes;
            var dbPath = Path.Combine(stagingDir, ROOT_DIR, BackupArchive.DB_ENTRY_NAME);
            return WriteStagedInternal(gz, dbPath, ref budget) ? null : ApiErrors.ARCHIVE_TOO_LARGE;
        }
        catch (InvalidDataException)
        {
            return ApiErrors.DAMAGED_ARCHIVE;
        }
    }

    // A tar is read in one pass, so every entry counts toward the limits, also the ones the import skips.
    private static string? StageTarInternal(Stream tar, string stagingDir, BackupLimits limits)
    {
        try
        {
            using var reader = new TarReader(tar, leaveOpen: true);
            var budget = limits.MaxUncompressedBytes;
            var count = 0;
            while (reader.GetNextEntry() is { } entry)
            {
                if (++count > BackupLimits.MAX_ENTRIES || entry.Length > budget)
                    return ApiErrors.ARCHIVE_TOO_LARGE;
                budget -= entry.Length;

                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile
                    or TarEntryType.ContiguousFile))
                    continue;

                if (entry.DataStream is not { } data || StagingPath(entry.Name) is not { } relative)
                    continue;

                // The entry's own length was taken from the budget above.
                if (!HasSpaceForStagingInternal(stagingDir, entry.Length))
                    return ApiErrors.NO_SPACE;

                var entryBudget = entry.Length;
                if (!WriteStagedInternal(data, Path.Combine(stagingDir, relative), ref entryBudget))
                    return ApiErrors.ARCHIVE_TOO_LARGE;
            }

            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or EndOfStreamException)
        {
            return ApiErrors.DAMAGED_ARCHIVE;
        }
    }

    private static string? StageSevenZipInternal(string path, string stagingDir, BackupLimits limits)
    {
        try
        {
            using var archive = SevenZipArchive.Open(path, new ReaderOptions { LookForHeader = false });
            if (archive.IsEncrypted)
                return ApiErrors.ENCRYPTED_ARCHIVE;

            long total = 0;
            var count = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.IsEncrypted)
                    return ApiErrors.ENCRYPTED_ARCHIVE;
                if (++count > BackupLimits.MAX_ENTRIES || entry.Size < 0 || entry.Size > limits.MaxUncompressedBytes - total)
                    return ApiErrors.ARCHIVE_TOO_LARGE;
                total += entry.Size;
            }

            if (!HasSpaceForStagingInternal(stagingDir, total))
                return ApiErrors.NO_SPACE;

            var budget = total;
            // One reader decompresses each solid block once; opening entries one by one would start it again each time.
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory || StagingPath(entry.Key ?? string.Empty) is not { } relative)
                    continue;

                using var source = reader.OpenEntryStream();
                if (!WriteStagedInternal(source, Path.Combine(stagingDir, relative), ref budget))
                    return ApiErrors.ARCHIVE_TOO_LARGE;
            }

            return null;
        }
        catch (SharpCompress.Common.CryptographicException)
        {
            return ApiErrors.ENCRYPTED_ARCHIVE;
        }
        catch (OutOfMemoryException)
        {
            // The archive asks for a dictionary larger than the bot can hold.
            return ApiErrors.ARCHIVE_TOO_LARGE;
        }
        catch (Exception ex) when (ex is SharpCompressException or InvalidDataException or EndOfStreamException
                                       or NotSupportedException or IndexOutOfRangeException
                                       or ArgumentOutOfRangeException or OverflowException)
        {
            Log.Warning(ex, "A 7z backup import could not be read");
            return ApiErrors.DAMAGED_ARCHIVE;
        }
    }

    private static string? StageDatabaseFileInternal(string uploadPath, string stagingDir)
    {
        File.Move(uploadPath, Path.Combine(stagingDir, ROOT_DIR, BackupArchive.DB_ENTRY_NAME));
        return null;
    }

    // False when the source has more than the budget; the budget shrinks by what was written.
    private static bool WriteStagedInternal(Stream source, string path, ref long budget)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 0
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = OWNER_RW;

        var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
        try
        {
            using var dest = new FileStream(path, options);
            int read;
            while ((read = source.Read(buffer, 0, BUFFER_SIZE)) > 0)
            {
                if (read > budget)
                    return false;

                budget -= read;
                dest.Write(buffer, 0, read);
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Applies the -wal beside the database, leaves one self-contained file and checks it.
    public static string? FinishDatabase(string dbPath)
    {
        if (!HasSqliteHeaderInternal(dbPath))
            return ApiErrors.NOT_SQLITE;

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();

        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();

            // The file comes from the uploader, so no view or trigger in it may run a function.
            cmd.CommandText = "PRAGMA trusted_schema=OFF";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read() || reader.GetInt32(0) != 0)
                    return ApiErrors.DB_DAMAGED;
            }

            cmd.CommandText = "PRAGMA journal_mode=DELETE";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "PRAGMA quick_check";
            return cmd.ExecuteScalar() is QUICK_CHECK_OK ? null : ApiErrors.DB_DAMAGED;
        }
        catch (SqliteException ex)
        {
            // SQLITE_FULL: the checkpoint needs room for the -wal.
            if (ex.SqliteErrorCode == SQLITE_FULL)
                return ApiErrors.NO_SPACE;

            Log.Warning(ex, "An imported database could not be opened");
            return ApiErrors.DB_DAMAGED;
        }
    }

    private static bool HasSqliteHeaderInternal(string path)
    {
        Span<byte> header = stackalloc byte[BackupArchive.SqliteHeader.Length];
        using var fs = File.OpenRead(path);
        return fs.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
               && header.SequenceEqual(BackupArchive.SqliteHeader);
    }
}
