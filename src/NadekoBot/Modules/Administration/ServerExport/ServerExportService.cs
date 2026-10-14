using System.Globalization;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.ServerData;

namespace NadekoBot.Modules.Administration.Services;

public enum ServerExportStatus
{
    Ok,
    Busy,
    TooLarge,
    NoData
}

public sealed class ServerExportFile(string path, string fileName, bool xpIncluded) : IDisposable
{
    public string Path { get; } = path;
    public string FileName { get; } = fileName;
    public bool XpIncluded { get; } = xpIncluded;

    public void Dispose()
        => ServerExportService.TryDelete(Path);
}

public readonly record struct ServerExportResult(ServerExportStatus Status, ServerExportFile? File = null);

public sealed class ServerExportService(DbService db, IBotCache cache, ShardData shardData) : INService, IReadyExecutor
{
    public static readonly TimeSpan EXPORT_COOLDOWN = TimeSpan.FromHours(24);
    public static readonly TimeSpan FAILED_EXPORT_COOLDOWN = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan EXPORT_SLOT_WAIT = TimeSpan.FromMinutes(5);

    // Discord allows 10 MiB for a file in a DM; the rest is room for the request.
    public const long MAX_DM_FILE_BYTES = 10_000_000;

    public const string FULL_EXPORT_DIR = "data/server-exports";
    private const string TEMP_DIR = "data/.server-export";
    private const string FILE_PREFIX = "nadeko-server-";
    private const string FILE_TIME_FORMAT = "yyyyMMdd-HHmmss";
    private const string ZIP_EXTENSION = ".zip";
    private const string DB_EXTENSION = ".db";

    // Each export reads the whole live database for one guild; one at a time keeps the load of a shard low.
    private readonly SemaphoreSlim _slot = new(1, 1);

    private string TempDir
        => Path.Combine(TEMP_DIR, $"shard-{shardData.ShardId}");

    public Task OnReadyAsync()
    {
        // Each shard has its own folder, so the files of another shard's running export stay.
        try
        {
            if (Directory.Exists(TempDir))
                Directory.Delete(TempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Failed to delete old server export files in {Dir}", TempDir);
        }

        return Task.CompletedTask;
    }

    private static TypedKey<long> CooldownKey(ulong guildId)
        => new($"serverexport:{guildId}");

    // Null when the guild got the export slot; else the time until the next export.
    public async Task<TimeSpan?> TryReserveAsync(ulong guildId)
    {
        var now = DateTime.UtcNow;
        var key = CooldownKey(guildId);
        if (await cache.AddAsync(key, (now + EXPORT_COOLDOWN).Ticks, EXPORT_COOLDOWN, overwrite: false))
            return null;

        var until = (await cache.GetAsync(key)).TryPickT0(out var ticks, out _) ? ticks : now.Ticks;
        var left = new DateTime(until, DateTimeKind.Utc) - now;
        return left > TimeSpan.Zero ? left : TimeSpan.FromSeconds(1);
    }

    // The export did no work, so the owner can try again at once.
    public async Task ReleaseAsync(ulong guildId)
        => await cache.RemoveAsync(CooldownKey(guildId));

    // The export did work and failed; a short wait stops a broken export from loading the database again and again.
    public async Task MarkFailedAsync(ulong guildId)
        => await cache.AddAsync(CooldownKey(guildId),
            (DateTime.UtcNow + FAILED_EXPORT_COOLDOWN).Ticks,
            FAILED_EXPORT_COOLDOWN);

    public Task<ServerExportResult> CreateForDmAsync(ulong guildId, string guildName)
        => CreateInternalAsync(guildId, guildName, MAX_DM_FILE_BYTES);

    // The full export of the bot owner, with xp, saved in the data folder.
    public async Task<(ServerExportStatus Status, string? Path)> SaveFullAsync(ulong guildId, string guildName)
    {
        var result = await CreateInternalAsync(guildId, guildName, long.MaxValue);
        if (result.File is not { } file)
            return (result.Status, null);

        using (file)
        {
            Directory.CreateDirectory(FULL_EXPORT_DIR);
            var dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(FULL_EXPORT_DIR, file.FileName));
            File.Move(file.Path, dest, overwrite: true);
            return (ServerExportStatus.Ok, dest);
        }
    }

    private async Task<ServerExportResult> CreateInternalAsync(ulong guildId, string guildName, long maxBytes)
    {
        if (!await _slot.WaitAsync(EXPORT_SLOT_WAIT))
            return new(ServerExportStatus.Busy);

        var id = Guid.NewGuid().ToString("N");
        var dbPath = System.IO.Path.Combine(TempDir, id + DB_EXTENSION);
        var zipPath = System.IO.Path.Combine(TempDir, id + ZIP_EXTENSION);
        var keepZip = false;
        try
        {
            Directory.CreateDirectory(TempDir);

            var livePath = ServerDataExporter.GetLiveDbPath(db);
            var stats = await Task.Run(() => ServerDataExporter.Build(livePath, guildId, dbPath));
            if (stats.Rows == 0)
                return new(ServerExportStatus.NoData);

            var now = DateTime.UtcNow;
            var manifest = new ServerExportManifest(guildId,
                guildName,
                now,
                StatsService.BotVersion,
                stats.Schema,
                XpIncluded: true);

            await Task.Run(() => ServerDataExporter.WriteZip(dbPath, manifest, zipPath));

            if (new FileInfo(zipPath).Length > maxBytes && stats.XpRows > 0)
            {
                manifest = manifest with { XpIncluded = false };
                TryDelete(zipPath);
                await Task.Run(() =>
                {
                    ServerDataExporter.DropXp(dbPath);
                    ServerDataExporter.WriteZip(dbPath, manifest, zipPath);
                });
            }

            if (new FileInfo(zipPath).Length > maxBytes)
                return new(ServerExportStatus.TooLarge);

            keepZip = true;
            var fileName = FILE_PREFIX
                           + guildId.ToString(CultureInfo.InvariantCulture)
                           + "-"
                           + now.ToString(FILE_TIME_FORMAT, CultureInfo.InvariantCulture)
                           + ZIP_EXTENSION;

            return new(ServerExportStatus.Ok, new ServerExportFile(zipPath, fileName, manifest.XpIncluded));
        }
        finally
        {
            TryDelete(dbPath);
            if (!keepZip)
                TryDelete(zipPath);

            _slot.Release();
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Failed to delete the server export file {Path}", path);
        }
    }
}
