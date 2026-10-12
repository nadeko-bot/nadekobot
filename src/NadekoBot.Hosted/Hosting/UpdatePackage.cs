using System.Formats.Tar;
using System.IO.Compression;

namespace NadekoBot.Hosted;

// Unpacks a release tarball (one top-level folder, as `tar -czf x.tar.gz nadeko-hosted-linux-x64` makes it).
public static class UpdatePackage
{
    public const string EXECUTABLE = "NadekoBot.Hosted";

    // A real package has a few thousand entries; the caps stop a crafted one from filling the disk.
    public const int MAX_ENTRIES = 100_000;
    public const int UNCOMPRESSED_FACTOR = 4;

    private const int BUFFER_SIZE = 81920;

    // Every entry takes at least one block on disk, and a file is rounded up to whole blocks.
    public const long DISK_BLOCK = 4096;

    private const UnixFileMode SPECIAL_BITS = UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit;
    private const UnixFileMode OWNER_RW = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OWNER_RWX = OWNER_RW | UnixFileMode.UserExecute;
    private const UnixFileMode EXEC_MODE = OWNER_RWX
                                           | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                                           | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public sealed class InvalidPackageException(string message) : Exception(message);

    public sealed class NoSpaceException() : Exception("the package does not fit on the disk");

    // Throws InvalidPackageException or NoSpaceException; destDir must not exist yet.
    public static async Task ExtractAsync(
        string tarGzPath,
        string destDir,
        long maxUncompressedBytes,
        long diskBudgetBytes,
        CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var root = Path.GetFullPath(destDir);

        string? top = null;
        var fileSizes = new Dictionary<string, long>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = 0;
        long total = 0;
        long disk = 0;

        await using var file = new FileStream(tarGzPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            BUFFER_SIZE, useAsync: true);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);

        try
        {
            while (await reader.GetNextEntryAsync(copyData: false, ct) is { } entry)
            {
                if (entry.EntryType == TarEntryType.GlobalExtendedAttributes)
                    continue;

                if (++entries > MAX_ENTRIES)
                    throw new InvalidPackageException("too many entries");

                var (entryTop, relative) = SplitNameInternal(entry.Name);
                if (entryTop is null)
                    continue;

                top ??= entryTop;
                if (!string.Equals(top, entryTop, StringComparison.Ordinal))
                    throw new InvalidPackageException($"more than one top-level entry: '{top}' and '{entryTop}'");

                if (relative is null)
                {
                    if (entry.EntryType != TarEntryType.Directory)
                        throw new InvalidPackageException($"top-level entry '{entryTop}' is not a directory");
                    continue;
                }

                if (!seen.Add(relative))
                    throw new InvalidPackageException($"duplicate entry '{relative}'");

                var path = Path.Combine(root, relative);
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile
                    or TarEntryType.ContiguousFile or TarEntryType.HardLink))
                    disk = AddDiskInternal(disk, 0, diskBudgetBytes);

                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(path);
                        SetModeInternal(path, (entry.Mode & ~SPECIAL_BITS) | OWNER_RWX);
                        break;

                    case TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile:
                        total = AddSizeInternal(total, entry.Length, maxUncompressedBytes);
                        disk = AddDiskInternal(disk, entry.Length, diskBudgetBytes);
                        await WriteFileInternalAsync(entry, path, ct);
                        SetModeInternal(path, FileModeInternal(relative, entry.Mode));
                        fileSizes.Add(relative, entry.Length);
                        break;

                    case TarEntryType.SymbolicLink:
                        if (!IsSafeLinkTargetInternal(entry.LinkName))
                            throw new InvalidPackageException($"symlink '{relative}' points outside the package");

                        EnsureParentInternal(path);
                        File.CreateSymbolicLink(path, entry.LinkName);
                        break;

                    case TarEntryType.HardLink:
                        // A hard link names another archive member, so it gets the same checks as an entry name.
                        var (linkTop, linkRelative) = SplitNameInternal(entry.LinkName);
                        if (linkTop != top || linkRelative is null || !fileSizes.TryGetValue(linkRelative, out var size))
                            throw new InvalidPackageException($"hard link '{relative}' does not point to an earlier file");

                        // Unpacked as a copy, so it takes the space of the file again.
                        total = AddSizeInternal(total, size, maxUncompressedBytes);
                        disk = AddDiskInternal(disk, size, diskBudgetBytes);
                        EnsureParentInternal(path);
                        File.Copy(Path.Combine(root, linkRelative), path);
                        SetModeInternal(path, FileModeInternal(relative, entry.Mode));
                        fileSizes.Add(relative, size);
                        break;

                    default:
                        throw new InvalidPackageException($"entry '{relative}' has the unsupported type {entry.EntryType}");
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or EndOfStreamException)
        {
            throw new InvalidPackageException($"not a gzipped tar: {ex.Message}");
        }
        // Entries which clash through a symlink, or a name taken twice, fail to create; only a full disk is not the package's fault.
        catch (IOException ex) when (ex is not (FileNotFoundException or DirectoryNotFoundException)
                                     && BackupArchive.HasFreeSpace(root, 0))
        {
            throw new InvalidPackageException($"cannot unpack: {ex.Message}");
        }

        if (!fileSizes.ContainsKey(EXECUTABLE))
            throw new InvalidPackageException($"no {EXECUTABLE} at the top of the package");
    }

    // Compared before adding, so a crafted size near long.MaxValue cannot overflow past the limit.
    private static long AddSizeInternal(long total, long size, long maxUncompressedBytes)
    {
        if (size < 0 || size > maxUncompressedBytes - total)
            throw new InvalidPackageException("the package unpacks to more than the limit");

        return total + size;
    }

    private static long AddDiskInternal(long disk, long size, long diskBudgetBytes)
    {
        var blocks = Math.Max(1, (size + DISK_BLOCK - 1) / DISK_BLOCK);
        if (blocks > (diskBudgetBytes - disk) / DISK_BLOCK)
            throw new NoSpaceException();

        return disk + blocks * DISK_BLOCK;
    }

    // Returns (null, null) for an entry that only names the archive root ("./").
    private static (string? Top, string? Relative) SplitNameInternal(string name)
    {
        if (name.Length == 0 || name[0] == '/' || name.Contains('\0') || name.Contains('\\'))
            throw new InvalidPackageException($"unsafe entry name '{name}'");

        var parts = name.Split('/');
        var start = 0;
        while (start < parts.Length && parts[start] == ".")
            start++;

        var end = parts.Length;
        if (end > start && parts[end - 1].Length == 0)
            end--;

        if (start == end)
            return (null, null);

        for (var i = start; i < end; i++)
        {
            if (parts[i] is "" or "." or "..")
                throw new InvalidPackageException($"unsafe entry name '{name}'");
        }

        var top = parts[start];
        return end - start == 1
            ? (top, null)
            : (top, string.Join('/', parts, start + 1, end - start - 1));
    }

    // Relative and without "..": however the links chain, resolving one only descends, so it stays in the package.
    private static bool IsSafeLinkTargetInternal(string target)
    {
        if (target.Length == 0 || target[0] == '/' || target.Contains('\0') || target.Contains('\\'))
            return false;

        foreach (var part in target.Split('/'))
        {
            if (part == "..")
                return false;
        }

        return true;
    }

    private static async Task WriteFileInternalAsync(TarEntry entry, string path, CancellationToken ct)
    {
        EnsureParentInternal(path);
        await using var dest = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            BUFFER_SIZE, useAsync: true);
        if (entry.DataStream is { } data)
            await data.CopyToAsync(dest, ct);
    }

    private static void EnsureParentInternal(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static UnixFileMode FileModeInternal(string relative, UnixFileMode mode)
    {
        mode = (mode & ~SPECIAL_BITS) | OWNER_RW;
        return relative == EXECUTABLE ? mode | EXEC_MODE : mode;
    }

    private static void SetModeInternal(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, mode);
    }
}
