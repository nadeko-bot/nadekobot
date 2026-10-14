using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace NadekoBot.Hosted;

public readonly record struct BackupLimits(long MaxUncompressedBytes)
{
    public const int MAX_ENTRIES = 10_000;

    // Bot data compresses well, but not 8 times past the import limit; more uncompressed data is a zip bomb.
    private const int MAX_COMPRESSION_RATIO = 8;

    // Archives in data/backups were made by the bot or passed the import check; only free space limits them.
    public static readonly BackupLimits Restore = new(long.MaxValue);

    public static BackupLimits ForImport(long maxArchiveBytes)
        => new(maxArchiveBytes * MAX_COMPRESSION_RATIO);
}

public static partial class BackupArchive
{
    public const string DB_ENTRY_NAME = "NadekoBot.db";
    public const string BACKUP_PREFIX = "nadeko-backup-";
    public const string BACKUP_EXTENSION = ".zip";
    public const string NAME_TIME_FORMAT = "yyyyMMdd-HHmmss";

    // Room for SQLite journals and the log while a restore or a backup writes its copy.
    public const long FREE_SPACE_MARGIN_BYTES = 64L * 1024 * 1024;

    public const string DATA_PREFIX = "data/";

    public const string MEDUSAE_DIR = "medusae";
    private const string SETTINGS_EXTENSION = ".yml";
    private const string XP_TEMPLATE_FILE = "xp_template.json";

    private static readonly FrozenSet<string> _excludedRootFiles = FrozenSet.ToFrozenSet(
        ["creds.yml", "creds_example.yml", "api.yml"],
        StringComparer.OrdinalIgnoreCase);

    public static ReadOnlySpan<byte> SqliteHeader
        => "SQLite format 3\0"u8;

    // The owner manages data/backups/ too, so a name like nadeko-backup-20261399-999999.zip can appear.
    public static bool IsValidBackupFileName(string fileName)
        => BackupFileNameRegex().IsMatch(fileName) && TryParseCreatedAtInternal(fileName, out _);

    // Only for names IsValidBackupFileName accepted.
    public static DateTime CreatedAt(string fileName)
        => TryParseCreatedAtInternal(fileName, out var createdAt)
            ? createdAt
            : throw new ArgumentException($"{fileName} is not a backup file name", nameof(fileName));

    private static bool TryParseCreatedAtInternal(string fileName, out DateTime createdAt)
        => DateTime.TryParseExact(
            fileName.AsSpan(BACKUP_PREFIX.Length, NAME_TIME_FORMAT.Length),
            NAME_TIME_FORMAT,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out createdAt);

    // Backups also carry medusae/ so the owner has a full copy, but a restore never writes code into the bot.
    public static bool IsBackupIncluded(string relativePath)
    {
        var slash = relativePath.IndexOf('/');
        if (slash < 0)
            return IsRestoreIncluded(relativePath);

        return relativePath.AsSpan(0, slash).Equals(MEDUSAE_DIR, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRestoreIncluded(string relativePath)
    {
        if (relativePath.Contains('/') || _excludedRootFiles.Contains(relativePath))
            return false;

        return relativePath.EndsWith(SETTINGS_EXTENSION, StringComparison.OrdinalIgnoreCase)
               || relativePath.Equals(XP_TEMPLATE_FILE, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeEntryName(string name)
    {
        if (string.IsNullOrEmpty(name)
            || name.AsSpan().ContainsAnyInRange('\0', '\x1f')
            || name.Contains('\x7f')
            || name.Contains('\\')
            || name.Contains(':')
            || name.StartsWith('/'))
            return false;

        // A trailing slash marks a directory entry; any other empty segment is not a real path.
        var path = name.EndsWith('/') ? name.AsSpan(0, name.Length - 1) : name.AsSpan();
        foreach (var range in path.Split('/'))
        {
            var segment = path[range];
            if (segment.IsEmpty || segment is "." or "..")
                return false;
        }

        return true;
    }

    public static bool IsDbEntry(string name)
        => name is DB_ENTRY_NAME or DATA_PREFIX + DB_ENTRY_NAME;

    // Validation and restore must agree on which entry is the database when an archive has both.
    public static ZipArchiveEntry? FindDbEntry(ZipArchive archive)
    {
        ZipArchiveEntry? underData = null;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0)
                continue;

            if (entry.FullName == DB_ENTRY_NAME)
                return entry;

            if (underData is null && entry.FullName == DATA_PREFIX + DB_ENTRY_NAME)
                underData = entry;
        }

        return underData;
    }

    public static string? RestoreEntryPath(string name)
    {
        if (!name.StartsWith(DATA_PREFIX, StringComparison.Ordinal) || name.EndsWith('/'))
            return null;

        var relative = name[DATA_PREFIX.Length..];
        return IsRestoreIncluded(relative) ? relative : null;
    }

    public static string? Validate(Stream zip, BackupLimits limits)
        => Validate(zip, limits, out _);

    public static string? Validate(Stream zip, BackupLimits limits, out long uncompressedBytes)
    {
        uncompressedBytes = 0;
        try
        {
            // ZipArchive loads every central directory entry up front, so the count is checked before it opens.
            var entryCount = ReadEntryCount(zip);
            if (entryCount < 0)
                return ApiErrors.NOT_A_ZIP;
            if (entryCount > BackupLimits.MAX_ENTRIES)
                return ApiErrors.ARCHIVE_TOO_LARGE;

            zip.Position = 0;
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > BackupLimits.MAX_ENTRIES)
                return ApiErrors.ARCHIVE_TOO_LARGE;

            var names = new HashSet<string>(archive.Entries.Count, StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                if (!IsSafeEntryName(entry.FullName) || !names.Add(entry.FullName))
                    return ApiErrors.UNSAFE_ENTRY;

                // Sizes come from the archive, so a forged one must not wrap the sum around.
                if (entry.Length > limits.MaxUncompressedBytes - uncompressedBytes)
                    return ApiErrors.ARCHIVE_TOO_LARGE;
                uncompressedBytes += entry.Length;

                if (RestoreEntryPath(entry.FullName) is not null && HasYamlAliasesInternal(entry))
                    return ApiErrors.UNSAFE_ENTRY;
            }

            if (FindDbEntry(archive) is not { } db)
                return ApiErrors.MISSING_DB;

            return HasSqliteHeader(db) ? null : ApiErrors.NOT_SQLITE;
        }
        catch (InvalidDataException)
        {
            return ApiErrors.NOT_A_ZIP;
        }
    }

    public static string? ValidateFile(string path, BackupLimits limits, out long uncompressedBytes)
    {
        using var fs = File.OpenRead(path);
        return Validate(fs, limits, out uncompressedBytes);
    }

    // Reads only the central directory and the database header, but on a slow disk that still blocks.
    public static Task<(string? Error, long UncompressedBytes)> ValidateFileAsync(string path, BackupLimits limits)
        => Task.Run(() => (ValidateFile(path, limits, out var uncompressed), uncompressed));

    public static bool HasFreeSpace(string dir, long bytes)
        => FreeBytes(dir) is not { } free || free >= bytes + FREE_SPACE_MARGIN_BYTES;

    public static long? FreeBytes(string dir)
    {
        try
        {
            return new DriveInfo(Path.GetFullPath(dir)).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not read the free space of {Dir}", dir);
            return null;
        }
    }

    private const uint EOCD_SIGNATURE = 0x06054b50;
    private const uint ZIP64_LOCATOR_SIGNATURE = 0x07064b50;
    private const uint ZIP64_EOCD_SIGNATURE = 0x06064b50;
    private const int EOCD_SIZE = 22;
    private const int EOCD_TOTAL_ENTRIES_OFFSET = 10;
    private const int MAX_ZIP_COMMENT = ushort.MaxValue;
    private const int ZIP64_LOCATOR_SIZE = 20;
    private const int ZIP64_LOCATOR_EOCD_OFFSET = 8;
    private const int ZIP64_EOCD_TOTAL_ENTRIES_OFFSET = 32;
    private const int ZIP64_EOCD_READ_SIZE = ZIP64_EOCD_TOTAL_ENTRIES_OFFSET + sizeof(ulong);

    // Total entries from the end of central directory record (APPNOTE 4.3.16), or from its zip64 form
    // when the 16-bit field is saturated. -1 when the stream has no such record.
    public static long ReadEntryCount(Stream zip)
    {
        var tailLength = (int)Math.Min(zip.Length, EOCD_SIZE + MAX_ZIP_COMMENT);
        if (tailLength < EOCD_SIZE)
            return -1;

        var tailStart = zip.Length - tailLength;
        var rented = ArrayPool<byte>.Shared.Rent(tailLength);
        try
        {
            var tail = rented.AsSpan(0, tailLength);
            zip.Position = tailStart;
            zip.ReadExactly(tail);

            for (var i = tailLength - EOCD_SIZE; i >= 0; i--)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(tail[i..]) != EOCD_SIGNATURE)
                    continue;

                var count = BinaryPrimitives.ReadUInt16LittleEndian(tail[(i + EOCD_TOTAL_ENTRIES_OFFSET)..]);
                if (count != ushort.MaxValue)
                    return count;

                return ReadZip64EntryCountInternal(zip, tailStart + i);
            }

            return -1;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static long ReadZip64EntryCountInternal(Stream zip, long eocdPosition)
    {
        if (eocdPosition < ZIP64_LOCATOR_SIZE)
            return -1;

        Span<byte> locator = stackalloc byte[ZIP64_LOCATOR_SIZE];
        zip.Position = eocdPosition - ZIP64_LOCATOR_SIZE;
        zip.ReadExactly(locator);
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != ZIP64_LOCATOR_SIGNATURE)
            return ushort.MaxValue;

        var recordPosition = BinaryPrimitives.ReadUInt64LittleEndian(locator[ZIP64_LOCATOR_EOCD_OFFSET..]);
        if (recordPosition > (ulong)(zip.Length - ZIP64_EOCD_READ_SIZE))
            return -1;

        Span<byte> record = stackalloc byte[ZIP64_EOCD_READ_SIZE];
        zip.Position = (long)recordPosition;
        zip.ReadExactly(record);
        if (BinaryPrimitives.ReadUInt32LittleEndian(record) != ZIP64_EOCD_SIGNATURE)
            return -1;

        var count = BinaryPrimitives.ReadUInt64LittleEndian(record[ZIP64_EOCD_TOTAL_ENTRIES_OFFSET..]);
        return count > long.MaxValue ? long.MaxValue : (long)count;
    }

    private static bool HasYamlAliasesInternal(ZipArchiveEntry entry)
    {
        if (!entry.FullName.EndsWith(SETTINGS_EXTENSION, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            using var reader = new StreamReader(entry.Open());
            return NadekoBot.Common.Yml.YamlHelper.ContainsAliases(reader);
        }
        catch (YamlDotNet.Core.YamlException)
        {
            // The bot rejects a broken file when it loads it, and a parser error ends the document before any alias.
            return false;
        }
    }

    public static bool HasSqliteHeader(ZipArchiveEntry entry)
    {
        Span<byte> header = stackalloc byte[SqliteHeader.Length];
        using var stream = entry.Open();
        stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return header.SequenceEqual(SqliteHeader);
    }

    [GeneratedRegex(@"\Anadeko-backup-\d{8}-\d{6}\.zip\z")]
    private static partial Regex BackupFileNameRegex();
}
