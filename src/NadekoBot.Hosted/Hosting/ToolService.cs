using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace NadekoBot.Hosted;

public readonly record struct ToolResult(string? Error, int Status, ToolManifest? Tool)
{
    public static readonly ToolResult NoSpace = new(ApiErrors.NO_SPACE, StatusCodes.Status507InsufficientStorage, null);
    public static readonly ToolResult Failed = new(ApiErrors.INTERNAL_ERROR, StatusCodes.Status500InternalServerError, null);
    public static readonly ToolResult Busy = new(ApiErrors.TOOL_INSTALL_IN_PROGRESS, StatusCodes.Status409Conflict, null);

    public static ToolResult Installed(ToolManifest tool)
        => new(null, StatusCodes.Status200OK, tool);

    public static ToolResult Invalid(string error)
        => new(error, StatusCodes.Status400BadRequest, null);

    public static ToolResult FromBadRequest(int status)
        => new(ApiErrors.CodeFor(status), status, null);
}

// Installs the yt-dlp, deno and ffmpeg which the manager sends, in data/tools. The bot uses a new tool from the next
// start of the program, without a restart. The previous version stays, because a running program can still use it.
public sealed class ToolService(HostPaths paths, ApiSettings settings, MediaTools tools, TimeProvider time)
{
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    private const string UPLOAD_FILE = ".upload.tmp";
    private const string STAGING_DIR = ".staging";
    private const int MAX_CHECK_OUTPUT = 64 * 1024;
    private const int DIGEST_PREFIX_LENGTH = 8;

    private int _busy;

    public IReadOnlyList<ToolManifest> List()
    {
        var list = new List<ToolManifest>(MediaTools.Names.Length);
        foreach (var name in MediaTools.Names)
        {
            if (tools.Manifest(name) is { } manifest)
                list.Add(manifest);
        }

        return list;
    }

    // The caller checks the name, the sha256, the version and the digest.
    public async Task<ToolResult> InstallAsync(
        string name,
        Stream body,
        long? contentLength,
        string sha256,
        string version,
        string digest,
        CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return ToolResult.Busy;

        var toolDir = tools.ToolDir(name);
        var upload = Path.Combine(toolDir, UPLOAD_FILE);
        var staging = Path.Combine(toolDir, STAGING_DIR);
        try
        {
            Directory.CreateDirectory(toolDir);
            DeleteFileInternal(upload);
            DeleteDirectoryInternal(staging);

            if (!BackupArchive.HasFreeSpace(paths.DataDir, contentLength ?? settings.MaxUpdateBytes))
                return ToolResult.NoSpace;

            string actual;
            try
            {
                actual = await UpdateService.ReceiveToFileAsync(body, upload, ct);
            }
            catch (BadHttpRequestException ex)
            {
                return ToolResult.FromBadRequest(ex.StatusCode);
            }

            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                return ToolResult.Invalid(ApiErrors.CHECKSUM_MISMATCH);

            try
            {
                await UpdatePackage.ExtractAsync(upload,
                    staging,
                    settings.MaxUpdateBytes * UpdatePackage.UNCOMPRESSED_FACTOR,
                    UnpackBudgetInternal(),
                    ct,
                    name);
            }
            catch (UpdatePackage.NoSpaceException)
            {
                return ToolResult.NoSpace;
            }
            catch (UpdatePackage.InvalidPackageException ex)
            {
                Log.Warning("Rejected a {Tool} package: {Reason}", name, ex.Message);
                return ToolResult.Invalid(ApiErrors.BAD_PACKAGE);
            }

            DeleteFileInternal(upload);

            // The check runs here, in the bot's own sandbox, so a tool which cannot run there never becomes current.
            if (!await CheckAsync(name, Path.Combine(staging, name), ct))
                return ToolResult.Invalid(ApiErrors.TOOL_CHECK_FAILED);

            var dirName = $"{version}-{digest.AsSpan(7, DIGEST_PREFIX_LENGTH)}";
            var previous = tools.Manifest(name);
            if (previous?.Dir == dirName)
                return ToolResult.Installed(previous);

            var final = Path.Combine(toolDir, dirName);
            DeleteDirectoryInternal(final);
            Directory.Move(staging, final);

            var manifest = new ToolManifest(name, version, digest, dirName, time.GetUtcNow().ToUnixTimeSeconds());
            SecretFile.WriteAllText(Path.Combine(toolDir, MediaTools.CURRENT_FILE), MediaTools.Format(manifest));
            PruneInternal(toolDir, dirName, previous?.Dir);

            Log.Information("Installed {Tool} {Version}", name, version);
            return ToolResult.Installed(manifest);
        }
        catch (IOException ex) when (!ct.IsCancellationRequested)
        {
            Log.Error(ex, "Installing {Tool} failed", name);
            return BackupArchive.HasFreeSpace(paths.DataDir, 0) ? ToolResult.Failed : ToolResult.NoSpace;
        }
        finally
        {
            DeleteFileInternal(upload);
            DeleteDirectoryInternal(staging);
            Volatile.Write(ref _busy, 0);
        }
    }

    // Returns false while an install runs. The bot uses the program on PATH again.
    public bool TryRemove(string name)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return false;

        try
        {
            var toolDir = tools.ToolDir(name);
            DeleteFileInternal(Path.Combine(toolDir, MediaTools.CURRENT_FILE));
            DeleteDirectoryInternal(toolDir);
            Log.Information("Removed the installed {Tool}", name);
            return true;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    public static Task<bool> CheckAsync(string name, string executable, CancellationToken ct)
        => name switch
        {
            MediaTools.YT_DLP => RunCheckInternalAsync(executable, ["--version"], static o => o.Trim().Length > 0, ct),
            MediaTools.DENO => RunCheckInternalAsync(executable,
                ["eval", "console.log(1+1)"],
                static o => o.Trim() == "2",
                ct),
            MediaTools.FFMPEG => RunCheckInternalAsync(executable,
                ["-hide_banner", "-protocols"],
                static o => o.Split('\n').Any(static l => l.Trim() == "https"),
                ct),
            _ => Task.FromResult(false)
        };

    private static async Task<bool> RunCheckInternalAsync(
        string executable,
        string[] args,
        Func<string, bool> isGood,
        CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var a in args)
            start.ArgumentList.Add(a);

        start.Environment["DENO_NO_UPDATE_CHECK"] = "1";
        start.Environment["DENO_NO_PROMPT"] = "1";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);

        Process? process = null;
        Task? stderr = null;
        try
        {
            process = Process.Start(start);
            if (process is null)
                return false;

            stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            var output = new char[MAX_CHECK_OUTPUT];
            var read = await process.StandardOutput.ReadBlockAsync(output.AsMemory(), timeout.Token);
            await process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await stderr;

            var good = process.ExitCode == 0 && isGood(new string(output, 0, read));
            if (!good)
                Log.Warning("{Executable} failed its check (exit code {Code})", executable, process.ExitCode);

            return good;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Warning("{Executable} did not finish its check within {Timeout}", executable, CheckTimeout);
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Log.Warning(ex, "Could not run {Executable}", executable);
            return false;
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

    private long UnpackBudgetInternal()
        => BackupArchive.FreeBytes(paths.DataDir) is { } free
            ? Math.Max(0, free - BackupArchive.FREE_SPACE_MARGIN_BYTES)
            : long.MaxValue;

    // Keeps the new version and the one it replaces; a program which started before the switch can still use it.
    private static void PruneInternal(string toolDir, string current, string? previous)
    {
        foreach (var dir in Directory.EnumerateDirectories(toolDir))
        {
            var name = Path.GetFileName(dir);
            if (name == current || name == previous || name == STAGING_DIR)
                continue;

            DeleteDirectoryInternal(dir);
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
