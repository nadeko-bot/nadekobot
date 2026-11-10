using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Serilog;
using YamlDotNet.Serialization;

namespace NadekoBot.Coordinator
{
    public enum HeartbeatResult
    {
        Ok,
        UnknownShard,
        Replaced
    }

    public sealed class CoordinatorRunner : BackgroundService
    {
        public const string SHARD_TOKEN_ENV = "NADEKOBOT_SHARD_TOKEN";

        private const string CONFIG_PATH = "coord.yml";

        private const string STATE_PATH = "coord-state.json";
        private const string LEGACY_STATE_PATH = "graceful.json";
        private const string LEGACY_STATE_BACKUP_PATH = "graceful_old.json";

        private const int UPTIME_WINDOW_SAMPLES = 480;
        private const int SHARD_ID_SHIFT = 22;

        // Process.StartTime comes from the boot time and the process ticks, which can round differently between reads.
        private static readonly TimeSpan _startTimeTolerance = TimeSpan.FromSeconds(2);

        private static readonly JsonSerializerOptions _stateJson = new() { WriteIndented = true };

        private readonly Deserializer _deserializer;

        // Held across a snapshot and its write, so Stop always writes or deletes the file last.
        private readonly Lock _stateFileLock = new();

        private Config _config;
        private ShardStatus[] _shardStatuses;
        private Queue<bool>[] _uptimeSamples;

        private readonly Lock locker = new();
        private readonly Random _rng;
        private bool _stopping;

        public CoordinatorRunner()
        {
            _deserializer = new();
            _config = LoadConfig();
            _rng = new Random();

            // A takeover can stop the running shards, so a config which cannot start new ones must stop the coordinator first.
            if (ConfigError(_config) is { } error)
                throw new InvalidOperationException($"{CONFIG_PATH}: {error}. The coordinator did not start and did not touch the shards");

            if (!TryTakeOverState() && !TryTakeOverLegacyState())
                InitAll();
        }

        private Config LoadConfig()
        {
            lock (locker)
            {
                return _deserializer.Deserialize<Config>(File.ReadAllText(CONFIG_PATH));
            }
        }

        public static Config ReadConfigFile()
            => new Deserializer().Deserialize<Config>(File.ReadAllText(CONFIG_PATH));

        private static string ConfigError(Config config)
        {
            if (config.TotalShards < 1)
                return "TotalShards must be 1 or more";

            if (string.IsNullOrWhiteSpace(config.ShardStartCommand))
                return "ShardStartCommand is empty";

            if (config.RecheckIntervalMs < 0 || config.UnresponsiveSec <= 0)
                return "RecheckIntervalMs must be 0 or more and UnresponsiveSec more than 0";

            return null;
        }

        public void ReloadConfig()
        {
            lock (locker)
            {
                var oldConfig = _config;
                var newConfig = LoadConfig();
                if (ConfigError(newConfig) is { } error)
                {
                    Log.Error("{Path}: {Error}. The coordinator keeps the old config", CONFIG_PATH, error);
                    return;
                }

                if (oldConfig.TotalShards != newConfig.TotalShards)
                {
                    KillAll();
                }
                _config = newConfig;
                if (oldConfig.TotalShards != newConfig.TotalShards)
                {
                    InitAll();
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    bool hadAction = false;
                    lock (locker)
                    {
                        if (_stopping)
                            return;

                        var shardIds = Enumerable.Range(0, 1) // shard 0 is always first
                            .Append((int)((117523346618318850UL >> SHARD_ID_SHIFT) % (ulong)_shardStatuses.Length)) // then nadeko server shard
                            .Concat(Enumerable.Range(1, _shardStatuses.Length - 1)
                                .OrderBy(_ => _rng.Next())) // then all other shards in a random order
                            .Distinct()
                            .ToList();

                        foreach (var shardId in shardIds)
                        {
                            if (stoppingToken.IsCancellationRequested)
                                break;

                            var status = _shardStatuses[shardId];

                            if (status.ShouldRestart)
                            {
                                Log.Warning("Shard {ShardId} is restarting (scheduled)...", shardId);
                                hadAction = true;
                                StartShard(shardId);
                                break;
                            }

                            if (DateTime.UtcNow - status.LastUpdate >
                                TimeSpan.FromSeconds(_config.UnresponsiveSec))
                            {
                                Log.Warning("Shard {ShardId} is restarting (unresponsive)...", shardId);
                                hadAction = true;
                                RecordUptimeSample(shardId, false);
                                StartShard(shardId);
                                break;
                            }

                            if (status.StateCounter > 8 && status.State != ConnState.Connected)
                            {
                                Log.Warning("Shard {ShardId} is restarting (stuck)...", shardId);
                                hadAction = true;
                                RecordUptimeSample(shardId, false);
                                StartShard(shardId);
                                break;
                            }

                            try
                            {
                                if (status.Process is null or { HasExited: true })
                                {
                                    Log.Warning("Shard {ShardId} is starting (process)...", shardId);
                                    hadAction = true;
                                    StartShard(shardId);
                                    break;
                                }
                            }
                            catch (InvalidOperationException)
                            {
                                Log.Warning("Process for shard {ShardId} is bugged... ", shardId);
                                hadAction = true;
                                StartShard(shardId);
                                break;
                            }
                        }

                    }

                    if (hadAction)
                        SaveState();

                    if (hadAction)
                    {
                        await Task.Delay(_config.RecheckIntervalMs, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Error(ex, "Error in coordinator: {Message}", ex.Message);
                }

                await Task.Delay(5000, stoppingToken).ConfigureAwait(false);
            }
        }

        private void StartShard(int shardId)
        {
            var status = _shardStatuses[shardId];
            KillInternal(status.Process);

            var token = Guid.NewGuid().ToString("N");
            var proc = StartShardProcess(shardId, token);
            _shardStatuses[shardId] = status with
            {
                Process = proc,
                Token = token,
                LastUpdate = DateTime.UtcNow,
                State = ConnState.Disconnected,
                ShouldRestart = false,
                StateCounter = 0,
            };
        }

        private Process StartShardProcess(int shardId, string token)
            => Process.Start(new ProcessStartInfo()
            {
                FileName = _config.ShardStartCommand,
                Arguments = string.Format(_config.ShardStartArgs,
                    shardId,
                    _shardStatuses.Length),
                EnvironmentVariables =
                {
                    {"NADEKOBOT_IS_COORDINATED", "1"},
                    // The pid is not enough: `dotnet run` starts the bot as a child process.
                    {SHARD_TOKEN_ENV, token}
                }
            });

        private static void KillInternal(Process process)
        {
            if (process is null)
                return;

            try
            {
                process.Kill(true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
            }

            process.Dispose();
        }

        public HeartbeatResult Heartbeat(int shardId, string token, int guildCount, ConnState state)
        {
            lock (locker)
            {
                if (shardId < 0 || shardId >= _shardStatuses.Length)
                    return HeartbeatResult.UnknownShard;

                var status = _shardStatuses[shardId];

                // Another process runs this shard now; two logins of one shard would fight over its gateway session.
                if (status.Token is null || !string.Equals(status.Token, token, StringComparison.Ordinal))
                    return HeartbeatResult.Replaced;

                RecordUptimeSample(shardId, state == ConnState.Connected);

                status = _shardStatuses[shardId] = status with
                {
                    GuildCount = guildCount,
                    State = state,
                    LastUpdate = DateTime.UtcNow,
                    StateCounter = status.State == state
                        ? status.StateCounter + 1
                        : 1,
                    UptimePercent = CalculateUptimePercent(shardId)
                };
                if (status.StateCounter > 1 && status.State == ConnState.Disconnected)
                {
                    Log.Warning("Shard {ShardId} is in DISCONNECTED state! ({StateCounter})",
                        status.ShardId,
                        status.StateCounter);
                }

                return HeartbeatResult.Ok;
            }
        }

        public bool IsShardProcess(int shardId, string token)
        {
            lock (locker)
            {
                return token is not null
                       && shardId >= 0
                       && shardId < _shardStatuses.Length
                       && string.Equals(_shardStatuses[shardId].Token, token, StringComparison.Ordinal);
            }
        }

        private void RecordUptimeSample(int shardId, bool isConnected)
        {
            var q = _uptimeSamples[shardId];
            q.Enqueue(isConnected);
            while (q.Count > UPTIME_WINDOW_SAMPLES)
                q.Dequeue();
        }

        private double CalculateUptimePercent(int shardId)
        {
            var q = _uptimeSamples[shardId];
            if (q.Count == 0)
                return 0;
            var connected = 0;
            foreach (var sample in q)
            {
                if (sample)
                    connected++;
            }
            return (double)connected / q.Count * 100;
        }

        public bool RestartShard(int shardId)
        {
            lock (locker)
            {
                if (shardId < 0 || shardId >= _shardStatuses.Length)
                    return false;

                _shardStatuses[shardId] = _shardStatuses[shardId] with
                {
                    ShouldRestart = true,
                    StateCounter = 0,
                };
                return true;
            }
        }

        public void RestartAll(bool nuke)
        {
            lock (locker)
            {
                if (nuke)
                {
                    KillAll();
                }

                QueueAll();
            }
        }

        private void KillAll()
        {
            lock (locker)
            {
                for (var shardId = 0; shardId < _shardStatuses.Length; shardId++)
                {
                    var status = _shardStatuses[shardId];
                    if (status.Process is null)
                        continue;

                    KillInternal(status.Process);
                    _shardStatuses[shardId] = status with
                    {
                        Process = null,
                        Token = null,
                        ShouldRestart = true,
                        LastUpdate = DateTime.UtcNow,
                        State = ConnState.Disconnected,
                        StateCounter = 0,
                    };
                }
            }
        }

        private bool SaveState()
        {
            lock (_stateFileLock)
            {
                CoordState snapshot;
                lock (locker)
                {
                    if (_stopping)
                        return false;

                    snapshot = SnapshotInternal();
                }

                return WriteState(snapshot);
            }
        }

        // Graceful: the shards keep running and the next coordinator takes them over. Else the shards stop too.
        public void Stop(bool graceful)
        {
            lock (_stateFileLock)
            {
                CoordState snapshot;
                lock (locker)
                {
                    _stopping = true;
                    if (!graceful)
                        KillAll();

                    snapshot = SnapshotInternal();
                }

                if (graceful)
                {
                    if (!WriteState(snapshot))
                        Log.Error("The shards keep running, but {Path} does not list them. Stop them by hand before the next start", STATE_PATH);
                    return;
                }

                try
                {
                    File.Delete(STATE_PATH);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Warning(ex, "Could not delete {Path}", STATE_PATH);
                }
            }
        }

        private CoordState SnapshotInternal()
        {
            var shards = new List<SavedShard>(_shardStatuses.Length);
            foreach (var s in _shardStatuses)
            {
                shards.Add(new SavedShard
                {
                    Pid = s.StartedAtUtc() is null ? null : s.Process.Id,
                    StartedAt = s.StartedAtUtc(),
                    Token = s.Token,
                    GuildCount = s.GuildCount,
                    ConnectionState = s.State
                });
            }

            return new CoordState { TotalShards = _shardStatuses.Length, Shards = shards };
        }

        // The temp file is moved over the old one, so a crash leaves the old state or the new one.
        private static bool WriteState(CoordState state)
        {
            var temp = $"{STATE_PATH}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(state, _stateJson));
                File.Move(temp, STATE_PATH, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error(ex, "Could not write {Path}. A coordinator crash would start the shards again", STATE_PATH);
                try
                {
                    File.Delete(temp);
                }
                catch (Exception deleteEx) when (deleteEx is IOException or UnauthorizedAccessException)
                {
                    Log.Warning(deleteEx, "Could not delete {Path}", temp);
                }

                return false;
            }
        }

        private bool TryTakeOverState()
        {
            if (!File.Exists(STATE_PATH))
                return false;

            CoordState saved;
            try
            {
                saved = JsonSerializer.Deserialize<CoordState>(File.ReadAllText(STATE_PATH));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Log.Error(ex, "Could not read {Path}. The coordinator starts each shard", STATE_PATH);
                return false;
            }

            if (saved?.Shards is null)
                return false;

            var processes = new Process[saved.Shards.Count];
            var running = 0;
            for (var i = 0; i < processes.Length; i++)
            {
                var shard = saved.Shards[i];
                if (shard.Pid is { } pid && shard.StartedAt is { } startedAt)
                    processes[i] = FindProcess(pid, p => Math.Abs((p.StartTime.ToUniversalTime() - startedAt).Ticks)
                                                         <= _startTimeTolerance.Ticks);
                if (processes[i] is not null)
                    running++;
            }

            if (saved.Shards.Count != _config.TotalShards)
            {
                Log.Warning("{Path} has {Saved} shards and coord.yml has {Total}. The coordinator stops the old shards",
                    STATE_PATH,
                    saved.Shards.Count,
                    _config.TotalShards);
                foreach (var p in processes)
                    KillInternal(p);
                return false;
            }

            // A state file without one live shard holds nothing which this start needs, and graceful.json may list the live ones.
            if (running == 0)
            {
                Log.Warning("{Path} lists no running shard", STATE_PATH);
                return false;
            }

            InitAll();
            for (var i = 0; i < processes.Length; i++)
            {
                var shard = saved.Shards[i];
                _shardStatuses[i] = _shardStatuses[i] with
                {
                    Process = processes[i],
                    Token = processes[i] is null ? null : shard.Token,
                    GuildCount = shard.GuildCount,
                    State = processes[i] is null ? ConnState.Disconnected : shard.ConnectionState
                };
            }

            Log.Information("Took over {Running} of {Total} running shards", running, processes.Length);
            return true;
        }

        private bool TryTakeOverLegacyState()
        {
            if (!File.Exists(LEGACY_STATE_PATH))
                return false;

            LegacyCoordState saved;
            DateTime savedAt;
            try
            {
                savedAt = File.GetLastWriteTimeUtc(LEGACY_STATE_PATH);
                saved = JsonSerializer.Deserialize<LegacyCoordState>(File.ReadAllText(LEGACY_STATE_PATH));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Without the pids the old shards cannot be stopped, and new ones would log in a second time.
                throw new InvalidOperationException(
                    $"Could not read {LEGACY_STATE_PATH}. The old shards keep running. Repair the file, or stop the old shards by hand and then remove it",
                    ex);
            }

            // The old coordinator always wrote one entry for each shard, so an empty list means the file lost the pids.
            if (saved?.StatusObjects is not { Count: > 0 } statuses)
                throw new InvalidOperationException(
                    $"{LEGACY_STATE_PATH} has no shard list. The old shards keep running. Repair the file, or stop the old shards by hand and then remove it");

            var processes = new Process[statuses.Count];
            var running = 0;
            for (var i = 0; i < processes.Length; i++)
            {
                processes[i] = statuses[i]?.Pid is { } pid
                    ? FindProcess(pid, p => p.StartTime.ToUniversalTime() <= savedAt)
                    : null;

                if (processes[i] is not { } process)
                    continue;

                // Outside the FindProcess check, so an unreadable environment stops the coordinator instead of losing the shard.
                if (IsCoordinatedShard(process.Id))
                {
                    running++;
                    continue;
                }

                process.Dispose();
                processes[i] = null;
            }

            if (statuses.Count != _config.TotalShards)
            {
                Log.Warning("{Path} has {Saved} shards and coord.yml has {Total}. The coordinator stops {Running} old shards and starts new ones",
                    LEGACY_STATE_PATH,
                    statuses.Count,
                    _config.TotalShards,
                    running);
                foreach (var p in processes)
                    KillInternal(p);

                MoveLegacyState();
                return false;
            }

            InitAll();
            // graceful.json lists one entry for each shard id, in order.
            for (var i = 0; i < _shardStatuses.Length; i++)
            {
                // Old shards do not send a token; they speak gRPC, so they stop heartbeating and get restarted one by one.
                _shardStatuses[i] = _shardStatuses[i] with
                {
                    Process = processes[i],
                    GuildCount = statuses[i]?.GuildCount ?? 0,
                    State = processes[i] is null ? ConnState.Disconnected : statuses[i].ConnectionState
                };
            }

            // The old file goes only once the new one holds the shards, so a crash in between never loses them.
            if (SaveState())
                MoveLegacyState();
            else
                Log.Error("The coordinator keeps {Path}, so its next start takes over the old shards again", LEGACY_STATE_PATH);

            Log.Information("Took over {Running} of {Total} shards of the gRPC coordinator", running, processes.Length);
            return true;
        }

        private static void MoveLegacyState()
        {
            try
            {
                File.Move(LEGACY_STATE_PATH, LEGACY_STATE_BACKUP_PATH, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error(ex, "Could not move {Path} to {Backup}. Remove it by hand", LEGACY_STATE_PATH, LEGACY_STATE_BACKUP_PATH);
            }
        }

        private const string COORDINATED_ENV_ENTRY = "NADEKOBOT_IS_COORDINATED=1";

        // graceful.json has no start time, so a reused pid could name any process; only a coordinated shard has this variable.
        private static bool IsCoordinatedShard(int pid)
        {
            if (!OperatingSystem.IsLinux())
                return true;

            try
            {
                var environ = File.ReadAllText($"/proc/{pid}/environ");
                foreach (var entry in environ.Split('\0'))
                {
                    if (string.Equals(entry, COORDINATED_ENV_ENTRY, StringComparison.Ordinal))
                        return true;
                }

                return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A shard which exited after FindProcess is no longer a shard to stop or take over.
                if (!Directory.Exists($"/proc/{pid}"))
                    return false;

                throw new InvalidOperationException(
                    $"Could not read /proc/{pid}/environ to check that pid {pid} is an old shard. The coordinator did not start and did not touch the shards",
                    ex);
            }
        }

        private static Process FindProcess(int pid, Func<Process, bool> isSame)
        {
            Process p = null;
            try
            {
                p = Process.GetProcessById(pid);
                if (!p.HasExited && isSame(p))
                    return p;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
            }

            p?.Dispose();
            return null;
        }

        private void InitAll()
        {
            lock (locker)
            {
                _shardStatuses = new ShardStatus[_config.TotalShards];
                _uptimeSamples = new Queue<bool>[_config.TotalShards];
                for (var shardId = 0; shardId < _shardStatuses.Length; shardId++)
                {
                    _shardStatuses[shardId] = new ShardStatus(shardId, DateTime.UtcNow);
                    _uptimeSamples[shardId] = new Queue<bool>(UPTIME_WINDOW_SAMPLES);
                }
            }
        }

        private void QueueAll()
        {
            lock (locker)
            {
                for (var shardId = 0; shardId < _shardStatuses.Length; shardId++)
                {
                    _shardStatuses[shardId] = _shardStatuses[shardId] with
                    {
                        ShouldRestart = true
                    };
                }
            }
        }

        // Uses the running shards, not the config, which a reshard changes before the shards restart.
        // Must match HostContext.OwnsGuild in NadekoBot.Hosted, or every shard answers 421.
        public void GetGuildShard(ulong guildId, out int shardId, out ConnState state, out bool scheduledForRestart)
        {
            lock (locker)
            {
                shardId = (int)((guildId >> SHARD_ID_SHIFT) % (ulong)_shardStatuses.Length);
                var status = _shardStatuses[shardId];
                state = status.State;
                scheduledForRestart = status.ShouldRestart;
            }
        }

        public int TotalShards()
        {
            lock (locker)
                return _shardStatuses.Length;
        }

        public int CountConnected(out int totalShards)
        {
            lock (locker)
            {
                totalShards = _shardStatuses.Length;
                var connected = 0;
                foreach (var status in _shardStatuses)
                {
                    if (status.State == ConnState.Connected && !status.ShouldRestart)
                        connected++;
                }

                return connected;
            }
        }

        public List<ShardStatus> GetAllStatuses()
        {
            lock (locker)
            {
                var toReturn = new List<ShardStatus>(_shardStatuses.Length);
                for (var i = 0; i < _shardStatuses.Length; i++)
                {
                    toReturn.Add(_shardStatuses[i] with
                    {
                        UptimePercent = CalculateUptimePercent(i)
                    });
                }
                return toReturn;
            }
        }
    }
}
