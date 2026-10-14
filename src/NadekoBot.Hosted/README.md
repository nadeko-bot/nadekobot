# NadekoBot.Hosted

NadekoBot.Hosted runs NadekoBot with a REST API. NadeKloud uses this API to
manage the bots which it hosts.

This is a separate program. It starts the bot in the same process. The
self-hosted `NadekoBot` program has no API and does not change.

## How it works

1. The program reads `data/api.yml` and starts the API.
2. It checks the Discord token, then starts the bot.
3. A manager can use the API before the bot is ready. Thus it can set a
   token, read the bot status, or restore a backup when the bot does not start.

Some actions stop the process: new creds, a restart, a backup restore and an
update. A supervisor (systemd with `Restart=always`, or the coordinator) must
start the process again.

## Run

```
NadekoBot.Hosted                    # standalone, shard 0
NadekoBot.Hosted run                # the same (the launcher uses this form)
NadekoBot.Hosted <shardId> <total>  # under the coordinator
NadekoBot.Hosted --version
```

Run it from the install folder. It uses the same files as the bot
(`data/creds.yml`, `data/NadekoBot.db`, `strings/`). It also uses
`data/api.yml`, which it creates on the first start:

```yaml
address: 127.0.0.1  # use 0.0.0.0 only when nothing but the manager can reach the machine
port: 5010          # under the coordinator, each shard uses port + shard id
apiKey: ''          # the manager sets it with the first PUT /api/creds
maxImportMb: 256    # largest backup which an import accepts
maxUpdateMb: 512    # largest release package which an update accepts
```

The program writes `api.yml` and `creds.yml` with mode `0600`. Backups never
contain them. When `api.yml` or the command line is not valid, the program
stops with exit code 2.

## Modes

- **Standalone** (default): one process, one bot, all routes. Each NadeKloud
  container uses this mode.
- **Coordinated** (the coordinator sets `NADEKOBOT_IS_COORDINATED=1`): each
  shard serves only `/api/health` and the server routes, on `port + shardId`.
  The routes which set xp currency rewards and the server activity route
  are only in standalone mode.
  For a server of a different shard, the shard answers `421 wrong_shard`.
  A shard without an `apiKey` runs with no API.

## Health

`GET /api/health` needs no key. It gives one of these statuses:

| status          | meaning                                                 |
| --------------- | ------------------------------------------------------- |
| `unconfigured`  | no API key yet                                          |
| `no_token`      | no Discord token yet                                    |
| `invalid_token` | Discord does not accept the token                       |
| `starting`      | the bot logs in and loads                               |
| `ok`            | the bot is ready                                        |
| `disconnected`  | the bot has no connection to Discord for 30 s or more   |
| `failed`        | the bot stopped its tries to start; `reason` tells why  |

## What the API does

- Sets the Discord token and the API key, and restarts the bot.
- Shows the bot status, its servers, and their channels and roles.
- Reads and changes bot settings and bot owners, and sets the owner's AI key.
- Sends the console lines, also while the bot does not start.
- Manages greets, expressions, quotes, warnings, warn punishments and xp.
- Shows which server features do not work because the bot cannot see a
  channel or give a role.
- Counts commands, command errors, joins and leaves for each server
  (standalone mode only, in `data/activity.db`, which backups do not contain).
- Sends a live feed of the commands which users run.
- Makes, downloads, imports and restores backups.
- Receives a new release. The supervisor installs it on the next start.

## Security

- Each request needs `Authorization: Bearer <apiKey>`.
- One request needs no key: the first `PUT /api/creds`, while `api.yml` has no
  key. Thus only the manager may reach the port.
- The key gives full control of the machine, because an update installs code.
  Keep the key as secret as a root password.
- The bot owner does not control the machine. The bot disables plugins
  (medusae), `.local`, local files and local network links.
- yt-dlp, ffmpeg and the music cache connect through a loopback proxy in the
  bot. The proxy connects only to public addresses, also after a redirect.

## Requirements

- A user without root rights, which can write only to `data/`.
- A port of 1024 or higher.
- `ffmpeg` and `yt-dlp` on `PATH`.
- A data volume without `noexec`, because the update check runs the new
  program.
