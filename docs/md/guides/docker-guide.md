---
title: NadekoBot Docker Guide
description: Run your own NadekoBot in a Docker container. Start it with Docker Compose or docker run, keep the data in a folder on your server and update it with one pull.
---

# NadekoBot Docker Guide

The NadekoBot image is `ghcr.io/nadeko-bot/nadekobot:v6`. It has ffmpeg, yt-dlp and Deno, so music works without more steps. The bot keeps its database and config files in `/app/data`.

## Before you start

- [Create a Discord bot](../creds-guide.md) and copy its token.
- Install [Docker Engine](https://docs.docker.com/engine/install/) with the Compose plugin.
- To keep the bot online all day, [get a Linux server](linux-server.md).
- The commands on this page are for Linux and macOS. On Windows, use Docker Desktop and type the commands in WSL.

## Start the bot

/// tab | Docker Compose

1. Make a folder for the bot and go into it:

    ```sh
    mkdir -p ~/nadeko && cd ~/nadeko
    ```

2. Create the file `compose.yml` (for example with `nano compose.yml`) with this text. Put your token in `bot_token` and your [time zone](https://en.wikipedia.org/wiki/List_of_tz_database_time_zones) in `TZ`:

    ```yaml
    services:
      nadeko:
        image: ghcr.io/nadeko-bot/nadekobot:v6
        container_name: nadeko
        restart: unless-stopped
        environment:
          TZ: Europe/Rome
          bot_token: YOUR_BOT_TOKEN
        volumes:
          - ./data:/app/data
    ```

3. Start the bot and show its log:

    ```sh
    docker compose up -d
    docker compose logs -f
    ```

///
/// tab | docker run

1. Make a folder for the bot and go into it:

    ```sh
    mkdir -p ~/nadeko && cd ~/nadeko
    ```

2. Start the bot. Put your token in place of `YOUR_BOT_TOKEN` and your [time zone](https://en.wikipedia.org/wiki/List_of_tz_database_time_zones) in place of `Europe/Rome`:

    ```sh
    docker run -d --name nadeko --restart unless-stopped \
      -e TZ=Europe/Rome \
      -e bot_token=YOUR_BOT_TOKEN \
      -v "$PWD/data:/app/data" \
      ghcr.io/nadeko-bot/nadekobot:v6
    ```

3. Show the log:

    ```sh
    docker logs -f nadeko
    ```

///

On the first start, the log shows lines like these. Then the bot comes online in your server.

```text
[12:17:22 INF] | #0 | Pid: 1
[12:17:23 INF] | #0 | Database does not exist. Creating a new database...
[12:17:24 INF] | #0 | Shard 0 logging in ...
[12:17:25 INF] | #0 | All services loaded in 1.24s
```

To stop the log, press ++ctrl+c++. The bot continues to run.

If the log shows `401: Unauthorized`, the token is wrong, and the container restarts again and again. Stop it with `docker stop nadeko`. Then put the correct token in `compose.yml` and type `docker compose up -d`. With `docker run`, type `docker rm -f nadeko` and run the command again with the correct token.

## Change the settings

The bot puts its files in the `data` folder: `creds.yml`, the [config files](config.md) and the [database](database.md) file `NadekoBot.db`. To change `creds.yml`, edit the file, then restart the bot:

```sh
nano data/creds.yml
docker restart nadeko
```

The bot writes these files as `root`. If your user is not `root`, use `sudo nano data/creds.yml`.

`bot_token` in the container settings replaces the `token` field of `creds.yml`. To change the token, change `bot_token` and start the container again as in step 2.

## Stop the bot

The bot restarts after `.die`, because of `restart: unless-stopped`. To stop the bot, type `docker stop nadeko`.

## Update the bot

/// tab | Docker Compose

```sh
cd ~/nadeko
docker compose pull
docker compose up -d
```

///
/// tab | docker run

```sh
cd ~/nadeko
docker pull ghcr.io/nadeko-bot/nadekobot:v6
docker rm -f nadeko
```

Then start the bot with the same `docker run` command as before.

///

The update keeps the `data` folder.
