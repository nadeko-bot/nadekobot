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
    public sealed class CoordinatorRunner : BackgroundService
    {
        private const string CONFIG_PATH = "coord.yml";

        private const string GRACEFUL_STATE_PATH = "graceful.json";
        private const string GRACEFUL_STATE_BACKUP_PATH = "graceful_old.json";

        private readonly Serializer _serializer;
        private readonly Deserializer _deserializer;

        private Config _config;
        private ShardStatus[] _shardStatuses;

        private readonly object locker = new object();
        private readonly Random _rng;
        private bool _gracefulImminent;
        
        public CoordinatorRunner()
        {
            _serializer = new();
            _deserializer = new();
            _config = LoadConfig();
            _rng = new Random();

           if(!TryRestoreOldState())
               InitAll();
        }
        
        private Config LoadConfig()
        {
            lock (locker)
            {
                return _deserializer.Deserialize<Config>(File.ReadAllText(CONFIG_PATH));
            }
        }

        private void SaveConfig(in Config config)
        {
            lock (locker)
            {
                var output = _serializer.Serialize(config);
                File.WriteAllText(CONFIG_PATH, output);
            }
        }

        public void ReloadConfig()
        {
            lock (locker)
            {
                var oldConfig = _config;
                var newConfig = LoadConfig();
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
            // Log.Information("Executing");

            bool first = true;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    bool hadAction = false;
                    lock (locker)
                    {
                        var shardIds = Enumerable.Range(0, 1) // shard 0 is always first
                            .Append((int)((117523346618318850 >> 22) % _config.TotalShards)) // then nadeko server shard
                            .Concat(Enumerable.Range(1, _config.TotalShards - 1)
                                .OrderBy(_ => _rng.Next())) // then all other shards in a random order
                            .Distinct()
                            .ToList();
                        
                        if (first)
                        {
                            // Log.Information("Startup order: {StartupOrder}",string.Join(' ', shardIds));
                            first = false;
                        }
                        
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
                                StartShard(shardId);
                                break;
                            }
                            
                            if (status.StateCounter > 8 && status.State != ConnState.Connected)
                            {
                                Log.Warning("Shard {ShardId} is restarting (stuck)...", shardId);
                                hadAction = true;
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
                    {
                        await Task.Delay(_config.RecheckIntervalMs, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in coordinator: {Message}", ex.Message);
                }

                await Task.Delay(5000, stoppingToken).ConfigureAwait(false);
            }
        }

        private void StartShard(int shardId)
        {
            var status = _shardStatuses[shardId];
            try
            {
                status.Process?.Kill(true);
            }
            catch
            {
            }
            try
            {
                status.Process?.Dispose();
            }
            catch
            {
            }

            var proc = StartShardProcess(shardId);
            _shardStatuses[shardId] = status with
            {
                Process = proc,
                LastUpdate = DateTime.UtcNow,
                State = ConnState.Disconnected,
                ShouldRestart = false,
                StateCounter = 0,
            };
        }

        private Process StartShardProcess(int shardId)
            => Process.Start(new ProcessStartInfo()
            {
                FileName = _config.ShardStartCommand,
                Arguments = string.Format(_config.ShardStartArgs,
                    shardId,
                    _config.TotalShards),
                EnvironmentVariables =
                {
                    {"NADEKOBOT_IS_COORDINATED", "1"}
                }
                // CreateNoWindow = true,
                // UseShellExecute = false,
            });

        public bool Heartbeat(int shardId, int guildCount, ConnState state)
        {
            lock (locker)
            {
                if (shardId >= _shardStatuses.Length)
                    throw new ArgumentOutOfRangeException(nameof(shardId));
                
                var status = _shardStatuses[shardId];
                status = _shardStatuses[shardId] = status with
                {
                    GuildCount = guildCount,
                    State = state,
                    LastUpdate = DateTime.UtcNow,
                    StateCounter = status.State == state
                        ? status.StateCounter + 1
                        : 1
                };
                if (status.StateCounter > 1 && status.State == ConnState.Disconnected)
                {
                    Log.Warning("Shard {ShardId} is in DISCONNECTED state! ({StateCounter})",
                        status.ShardId,
                        status.StateCounter);
                }

                return _gracefulImminent;
            }
        }

        public void SetShardCount(int totalShards)
        {
            lock (locker)
            {
                SaveConfig(new Config(
                    totalShards,
                    _config.RecheckIntervalMs,
                    _config.ShardStartCommand,
                    _config.ShardStartArgs,
                    _config.UnresponsiveSec));
            }
        }

        public void RestartShard(int shardId, bool queue)
        {
            lock (locker)
            {
                if (shardId >= _shardStatuses.Length)
                    throw new ArgumentOutOfRangeException(nameof(shardId));

                _shardStatuses[shardId] = _shardStatuses[shardId] with
                {
                    ShouldRestart = true,
                    StateCounter = 0,
                };
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
                    if (status.Process is Process p)
                    {
                        try{p.Kill();} catch {}
                        try{p.Dispose();} catch {}
                        _shardStatuses[shardId] = status with
                        {
                            Process = null,
                            ShouldRestart = true,
                            LastUpdate = DateTime.UtcNow,
                            State = ConnState.Disconnected,
                            StateCounter = 0,
                        };
                    }
                }
            }
        }

        public void SaveState()
        {
            var coordState = new CoordState()
            {
                StatusObjects = _shardStatuses
                    .Select(x => new JsonStatusObject()
                    {
                        Pid = x.Process?.Id,
                        ConnectionState = x.State,
                        GuildCount = x.GuildCount,
                    })
                    .ToList()
            };
            var jsonState = JsonSerializer.Serialize(coordState, new JsonSerializerOptions()
            {
                WriteIndented = true,
            });
            File.WriteAllText(GRACEFUL_STATE_PATH, jsonState);
        }
        private bool TryRestoreOldState()
        {
            lock (locker)
            {
                if (!File.Exists(GRACEFUL_STATE_PATH))
                    return false;

                Log.Information("Restoring old coordinator state...");
                
                CoordState savedState;
                try
                {
                    savedState = JsonSerializer.Deserialize<CoordState>(File.ReadAllText(GRACEFUL_STATE_PATH));

                    if (savedState is null)
                        throw new Exception("Old state is null?!");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error deserializing old state: {Message}", ex.Message);
                    File.Move(GRACEFUL_STATE_PATH, GRACEFUL_STATE_BACKUP_PATH, overwrite: true);
                    return false;
                }

                if (savedState.StatusObjects.Count != _config.TotalShards)
                {
                    Log.Error("Unable to restore old state because shard count doesn't match");
                    File.Move(GRACEFUL_STATE_PATH, GRACEFUL_STATE_BACKUP_PATH, overwrite: true);
                    return false;
                }

                _shardStatuses = new ShardStatus[_config.TotalShards];

                for (int shardId = 0; shardId < _shardStatuses.Length; shardId++)
                {
                    var statusObj = savedState.StatusObjects[shardId];
                    Process p = null;
                    if (statusObj.Pid is { } pid)
                    {
                        try
                        {
                            p = Process.GetProcessById(pid);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Process for shard {ShardId} is not runnning", shardId);
                        }
                    }

                    _shardStatuses[shardId] = new(
                        shardId,
                        DateTime.UtcNow,
                        statusObj.GuildCount,
                        statusObj.ConnectionState,
                        p is null,
                        p);
                }
                
                File.Move(GRACEFUL_STATE_PATH, GRACEFUL_STATE_BACKUP_PATH, overwrite: true);
                Log.Information("Old state restored!");
                return true;
            }
        }

        private void InitAll()
        {
            lock (locker)
            {
                _shardStatuses = new ShardStatus[_config.TotalShards];
                for (var shardId = 0; shardId < _shardStatuses.Length; shardId++)
                {
                    _shardStatuses[shardId] = new ShardStatus(shardId, DateTime.UtcNow);
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


        public ShardStatus GetShardStatus(int shardId)
        {
            lock (locker)
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(shardId, _shardStatuses.Length);

                return _shardStatuses[shardId];
            }
        }

        public List<ShardStatus> GetAllStatuses()
        {
            lock (locker)
            {
                var toReturn = new List<ShardStatus>(_shardStatuses.Length);
                toReturn.AddRange(_shardStatuses);
                return toReturn;
            }
        }

        public void PrepareGracefulShutdown()
        {
            lock (locker)
            {
                _gracefulImminent = true;
            }
        }

        public string GetConfigText()
            => File.ReadAllText(CONFIG_PATH);

        public void SetConfigText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentNullException(nameof(text), "coord.yml can't be empty");
            var config = _deserializer.Deserialize<Config>(text);
            SaveConfig(in config);
            ReloadConfig();
        }
    }
}