# Coordinator project

Starts the shards of a sharded NadekoBot, watches them and restarts them. Each shard is its own process, so one shard which crashes or hangs never stops the others.

### Supports
- Checking status
- Individual shard restarts
- Full shard restarts
- Coordinator restarts and crashes without stopping the shards
- Kill/Stop
- A dashboard API which sends each server request to the shard of that server

To give each shard the REST API, set `ShardStartCommand` to the `NadekoBot.Hosted` executable (instead of `NadekoBot`) and `ShardStartArgs` to `{0} {1}`. See `src/NadekoBot.Hosted/README.md`.

## Shard protocol

The shards and the bot's owner commands call the coordinator with JSON over HTTP/2 without TLS ("prior knowledge"), at `coordinatorUrl` in the bot's `creds.yml` (default `http://localhost:3442`). The coordinator's `appsettings.json` sets the port. It needs no package and no certificate. The routes have no key, so the port must stay on loopback.

| route | job |
| ----- | --- |
| `POST /coord/shards/{id}/heartbeat` `{ token, guildCount, state }` | every 7.5 s from each shard; `204`, `404 unknown_shard`, or `409 replaced` when another process runs this shard |
| `GET /coord/shards` | the `GET /api/status` body; `.shardstats` and the server count of `.stats` |
| `POST /coord/shards/{id}/restart` | `202`; `.restartshard` |
| `POST /coord/restart-all` `{ nuke }` | `202`; `.restart`. The shards restart one at a time; `nuke` kills them first |
| `POST /coord/die` `{ graceful }` | `202`, then the coordinator exits. Graceful keeps the shards running; else it stops them too |
| `POST /coord/reload` | `204`; reads `coord.yml` and `data/api.yml` again (`.coordreload`) |
| `POST /coord/votes` `{ userId, type }` | `202`; VotesApi gives a vote (`topgg`, `discordbotlist`, `discords`); `400 invalid_value`, or `503 queue_full` when 1,000 votes wait |
| `POST /coord/votes/take` `{ token }` | shard 0 takes up to 50 votes: `{ votes: [{ userId, type }] }`. Without a vote, it answers after 5 s with an empty list. `409 replaced` for any other process |

The coordinator gives each shard process a token in `NADEKOBOT_SHARD_TOKEN`. A heartbeat with another token gets `409`, and that process exits, so one shard never logs in twice.

## Votes

VotesApi gives each vote to the coordinator, and shard 0 takes the votes in a loop and gives the rewards. The coordinator keeps the votes in memory while shard 0 restarts. When the coordinator stops, it loses the votes which still wait. A bot without the coordinator gives no vote rewards.

## Crashes and restarts

- After each shard start, the coordinator writes `coord-state.json`: the pid, the start time and the token of each shard.
- At each start, the coordinator takes over each saved shard which still runs with the same start time. When the shard count in `coord.yml` changed, it stops those shards and starts new ones.
- A shard keeps running while the coordinator does not answer. After 5 minutes alone it exits.
- The coordinator's systemd unit needs `KillMode=process`. Otherwise systemd kills the shards when the coordinator stops or crashes.
- A unit which starts the coordinator in tmux (`Type=forking`) also needs `PIDFile=`. After a takeover, the shards which the coordinator took over are children of systemd too. Without the file, systemd cannot tell which process is the main one. Then `systemctl stop` stops nothing, and `Restart=` misses a crash. Write the file with `tmux new-session -d -P -F '#{pid}' ... > <file>`.
- The shards and their ffmpeg processes ignore SIGHUP, so they keep running when a coordinator which runs in a terminal (tmux, screen) exits.
- After `systemctl stop <unit>`, the shards keep running. To stop them at once, kill their processes, for example with `pkill -x NadekoBot`. Ctrl-C in the coordinator's terminal stops the coordinator and the shards which it started, but not the shards which it took over.
- While the coordinator runs, it starts each shard whose process ends. Thus, stop the coordinator first and the shards after it. A `pkill` while the coordinator runs only restarts the shards.
- With `Restart=always`, systemd starts the coordinator again when it exits for any reason other than `systemctl stop`: Ctrl-C, `kill`, `.die` or a crash.
- The terminal of a new coordinator does not show the console output of the shards which it took over. Read their logs in Seq (`seq` in `creds.yml`). Without Seq, their console output is lost.

## Update from the gRPC coordinator

The old shards speak gRPC. The new coordinator speaks only HTTP/2 and cannot read their heartbeats. When it starts an old bot, it restarts each shard after `UnresponsiveSec`, again and again. Thus, `ShardStartCommand` must start the new bot.

No full outage is needed, unless the old coordinator runs in a terminal (tmux, screen). Then the old shards stop with it, because they do not ignore SIGHUP. In that case:

1. Run `systemctl stop <unit>`. Do not stop the coordinator with Ctrl-C or `kill`: with `Restart=always`, systemd starts it again.
2. Check that no old shard runs (`pgrep -x NadekoBot`). Stop the rest with `pkill -x NadekoBot`.
3. Install the new coordinator, the new bot and the new VotesApi. Point `ShardStartCommand` in `coord.yml` to the new bot.
4. Start the unit again. Every shard starts again, one at a time.
5. Restart VotesApi.

Otherwise:

1. Set `KillMode=process` in the coordinator's systemd unit and run `systemctl daemon-reload`. Without it, systemd stops every shard when the old coordinator exits.
2. Install the new coordinator, the new bot and the new VotesApi. If the new bot is in another folder, point `ShardStartCommand` in `coord.yml` to it.
3. Keep `TotalShards` in `coord.yml` at the old count.
4. Run `.die true` (graceful). The old shards keep running and wait for a coordinator. If the unit has `Restart=always`, also run `systemctl stop <unit>`. Otherwise systemd starts a coordinator again by itself.
5. Start the new coordinator in the old coordinator's working folder, which holds `graceful.json` and `coord.yml`. From another folder it finds no `coord.yml` and stops. With a copied `coord.yml` but no `graceful.json`, it starts a second process for each shard. It takes over the shards in `graceful.json`, writes `coord-state.json` and then renames `graceful.json` to `graceful_old.json`. The log says "Took over N of M shards of the gRPC coordinator".
6. The old shards send no heartbeat to the new coordinator. After `UnresponsiveSec` the coordinator restarts them one at a time on the new bot, about every `RecheckIntervalMs` + 5 s. With 32 shards and the default settings, this takes about 6 minutes.
7. Check with `ps` that one process runs for each shard, never two.
8. Restart VotesApi. It sends the votes to `CoordinatorUrl` in its `appsettings.json` (default `http://localhost:3442`). It ignores the old `BotGrpcHost`, so you can delete that line.

When something does not match:
- A different `TotalShards`: the coordinator stops every old shard at once and starts the new count one at a time. The bot is offline until the new shards connect.
- A `graceful.json` which it cannot read, or which has no shard list: the coordinator stops at once and kills nothing. The old shards keep running. Repair the file, or stop the old shards by hand (`ps`, `kill`) and then remove it. Never remove a `graceful.json` which lists running shards: the next start begins a second process for each of them.
- A shard process whose environment it cannot read in `/proc`: the coordinator stops at once and kills nothing.
- A `coord.yml` with `TotalShards` below 1, an empty `ShardStartCommand`, a negative `RecheckIntervalMs` or an `UnresponsiveSec` of 0 or less: the coordinator stops at once and touches nothing. `.coordreload` keeps the old config.

## Dashboard API

Set `ApiAddress` and `ApiPort` in `coord.yml` to turn it on. The API listens on its own port (HTTP/1.1), separate from the shard port. Use a private address: only the dashboard may reach it. A `/coord` path on this port, or an `/api` path on the shard port, gets `404`.

The coordinator reads `data/api.yml` of the shards (relative to its working folder): `apiKey` and `port`. Each shard listens on `port + shardId`. Without a key, every request except health gets `401`. `POST /coord/reload` reads `data/api.yml` again. A change of `ApiAddress` or `ApiPort` needs a coordinator restart; the shards keep running.

Each request needs `Authorization: Bearer <apiKey>`, except `GET /api/health`. Errors are `{"error":"<code>"}`.

| route | answer |
| ----- | ------ |
| `GET /api/health` | `{ status, totalShards, connectedShards }`; `status` is `ok` (all shards connected), `degraded` or `disconnected` (none) |
| `GET /api/status` | `{ totalShards, connectedShards, guildCount, shards: [{ shardId, state, guildCount, uptimePercent, startedAt, scheduledForRestart, lastUpdate }] }` |
| `GET /api/guilds/{guildId}/shard` | `{ shardId, state }`; `state` is `connected`, `connecting`, `disconnected` or `restarting` |
| `POST /api/shards/{shardId}/restart` | `202`; queues a restart; at most 5 for all shards in a minute (`429 rate_limited` with `Retry-After`) |
| `/api/guilds/{guildId}/...` (any other) | sent to shard `(guildId >> 22) % totalShards`, with the same key |

- `404 not_found`: a guild id which is not plain digits, or a shard id outside the shard list.
- `503 not_ready`: the shard of the server is not connected, or waits for a restart.
- `502 shard_unavailable`: the shard did not answer.
- The shard answers the rest (`401`, `404`, `421`, its own errors), and the coordinator passes it on.
