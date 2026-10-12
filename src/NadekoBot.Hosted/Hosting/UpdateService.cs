using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace NadekoBot.Hosted;

public readonly record struct UpdateResult(string? Error, int Status, string? Version)
{
    public static readonly UpdateResult NoSpace = new(ApiErrors.NO_SPACE, StatusCodes.Status507InsufficientStorage, null);
    public static readonly UpdateResult Failed = new(ApiErrors.INTERNAL_ERROR, StatusCodes.Status500InternalServerError, null);

    public static UpdateResult Accepted(string version)
        => new(null, StatusCodes.Status202Accepted, version);

    public static UpdateResult Invalid(string error)
        => new(error, StatusCodes.Status400BadRequest, null);

    public static UpdateResult Conflict(string error)
        => new(error, StatusCodes.Status409Conflict, null);

    public static UpdateResult FromBadRequest(int status)
        => new(ApiErrors.CodeFor(status), status, null);

    public static UpdateResult From(BackupResult busy)
        => new(busy.Error, busy.Status, null);
}

// The bot cannot replace its running files, so it only stages a release for the supervisor.
public sealed class UpdateService(HostPaths paths, ApiSettings settings, BackupService backups, BootGuard boot)
{
    // Older versions have no PUT /api/update, so the manager could never update such a bot again.
    public static readonly Version MinVersion = new(7, 6, 1);

    public static readonly TimeSpan VersionCheckTimeout = TimeSpan.FromSeconds(30);

    private const int BUFFER_SIZE = 81920;
    private const int MAX_VERSION_LENGTH = 32;
    private const int SHA256_HEX_LENGTH = 64;

    private static readonly SearchValues<char> _hexChars = SearchValues.Create("0123456789abcdefABCDEF");
    private static readonly SearchValues<char> _versionChars = SearchValues.Create("0123456789.");

    public async Task<UpdateResult> StageAsync(
        Stream body,
        long? contentLength,
        string sha256,
        string version,
        CancellationToken ct)
    {
        // A new version is on trial until it gets ready; a second update now would make its rollback target wrong.
        if (File.Exists(paths.UpdateTrial))
            return UpdateResult.Conflict(ApiErrors.UPDATE_TRIAL_PENDING);

        if (backups.TryBeginUpdate() is { } busy)
            return UpdateResult.From(busy);

        var staged = false;
        try
        {
            // A marker left by an earlier request would point the supervisor at the half-written tree below.
            DeleteFileInternal(paths.UpdatePending);
            DeleteFileInternal(paths.UpdateUpload);
            DeleteDirectoryInternal(paths.UpdateStaged);
            Directory.CreateDirectory(paths.UpdateDir);

            if (!BackupArchive.HasFreeSpace(paths.DataDir, contentLength ?? settings.MaxUpdateBytes))
                return UpdateResult.NoSpace;

            string actual;
            try
            {
                actual = await ReceiveInternalAsync(body, ct);
            }
            catch (BadHttpRequestException ex)
            {
                return UpdateResult.FromBadRequest(ex.StatusCode);
            }

            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                return UpdateResult.Invalid(ApiErrors.CHECKSUM_MISMATCH);

            try
            {
                await UpdatePackage.ExtractAsync(paths.UpdateUpload,
                    paths.UpdateStaged,
                    settings.MaxUpdateBytes * UpdatePackage.UNCOMPRESSED_FACTOR,
                    UnpackBudgetInternal(),
                    ct);
            }
            catch (UpdatePackage.NoSpaceException)
            {
                return UpdateResult.NoSpace;
            }
            catch (UpdatePackage.InvalidPackageException ex)
            {
                Log.Warning("Rejected an update package: {Reason}", ex.Message);
                return UpdateResult.Invalid(ApiErrors.BAD_PACKAGE);
            }

            DeleteFileInternal(paths.UpdateUpload);

            var reported = await ReadVersionInternalAsync(Path.Combine(paths.UpdateStaged, UpdatePackage.EXECUTABLE), ct);
            if (reported != version)
            {
                Log.Warning("The update package reports version {Reported}, not {Version}", reported, version);
                return UpdateResult.Invalid(ApiErrors.VERSION_MISMATCH);
            }

            SecretFile.WriteAllText(paths.UpdatePending, $"version={version}\nsha256={actual}\n");
            boot.Reset();
            staged = true;
            Log.Information("Version {Version} is staged. Restarting so the supervisor applies it", version);
            return UpdateResult.Accepted(version);
        }
        catch (IOException ex) when (!ct.IsCancellationRequested)
        {
            Log.Error(ex, "Staging an update failed");
            return BackupArchive.HasFreeSpace(paths.DataDir, 0) ? UpdateResult.Failed : UpdateResult.NoSpace;
        }
        finally
        {
            DeleteFileInternal(paths.UpdateUpload);
            if (!staged)
            {
                DeleteDirectoryInternal(paths.UpdateStaged);
                // The lock stays held after a success: the process exits next, and nothing may run before it does.
                backups.EndUpdate();
            }
        }
    }

    public static bool IsValidSha256(ReadOnlySpan<char> value)
        => value.Length == SHA256_HEX_LENGTH && !value.ContainsAnyExcept(_hexChars);

    // Exactly major.minor.patch, digits only.
    public static bool TryParseVersion(ReadOnlySpan<char> value, out Version version)
    {
        version = MinVersion;
        if (value.Length is 0 or > MAX_VERSION_LENGTH || value.ContainsAnyExcept(_versionChars))
            return false;

        Span<Range> parts = stackalloc Range[4];
        if (value.Split(parts, '.') != 3)
            return false;

        Span<int> numbers = stackalloc int[3];
        for (var i = 0; i < numbers.Length; i++)
        {
            if (!int.TryParse(value[parts[i]], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    // Removes a stage which no supervisor applied; the supervisor's rollback snapshots stay.
    public static void DiscardLeftovers(HostPaths paths)
    {
        if (File.Exists(paths.UpdatePending))
            Log.Warning("An update was staged but not applied. Is the supervisor's update step installed?");

        DeleteFileInternal(paths.UpdatePending);
        DeleteFileInternal(paths.UpdateUpload);
        DeleteDirectoryInternal(paths.UpdateStaged);
    }

    // The supervisor copies the databases, then the shipped assets of the new tree, so the tree needs its size twice.
    private long UnpackBudgetInternal()
    {
        if (BackupArchive.FreeBytes(paths.DataDir) is not { } free)
            return long.MaxValue;

        long databases = 0;
        try
        {
            foreach (var db in Directory.EnumerateFiles(paths.DataDir, "*.db*"))
                databases += new FileInfo(db).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not read the database sizes in {Dir}", paths.DataDir);
        }

        return Math.Max(0, (free - BackupArchive.FREE_SPACE_MARGIN_BYTES - databases) / 2);
    }

    private async Task<string> ReceiveInternalAsync(Stream body, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
        try
        {
            await using var dest = new FileStream(paths.UpdateUpload, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, BUFFER_SIZE, useAsync: true);
            int read;
            while ((read = await body.ReadAsync(buffer.AsMemory(0, BUFFER_SIZE), ct)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // Any failure to run it (not executable, wrong architecture, a hang) counts as the wrong version.
    private static async Task<string?> ReadVersionInternalAsync(string executable, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            ArgumentList = { "--version" },
            WorkingDirectory = Path.GetDirectoryName(executable),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(VersionCheckTimeout);

        Process? process = null;
        Task? stderr = null;
        try
        {
            process = Process.Start(start);
            if (process is null)
                return null;

            // Only the start of the output can be a version; the rest is drained so the process can exit.
            stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            var head = new char[MAX_VERSION_LENGTH + 2];
            var read = await process.StandardOutput.ReadBlockAsync(head.AsMemory(), timeout.Token);
            await process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await stderr;
            return process.ExitCode == 0 ? head.AsSpan(0, read).Trim().ToString() : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Warning("{Executable} --version did not finish within {Timeout}", executable, VersionCheckTimeout);
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Log.Warning(ex, "Could not run {Executable} --version", executable);
            return null;
        }
        finally
        {
            if (process is { HasExited: false })
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Log.Warning(ex, "Could not stop {Executable}", executable);
                }
            }

            if (stderr is not null)
            {
                try
                {
                    await stderr;
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException)
                {
                }
            }

            process?.Dispose();
        }
    }

    private static void DeleteFileInternal(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not delete {Path}", path);
        }
    }

    private static void DeleteDirectoryInternal(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not delete {Path}", path);
        }
    }
}
