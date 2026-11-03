---
title: Get a Linux server (DigitalOcean VPS)
description: Rent a small Linux server to keep your NadekoBot online all day. Create a DigitalOcean droplet for $6 per month and connect to it with SSH.
---

# Get a Linux server

The bot stops when the computer that runs it stops. To keep the bot online all day, rent a small Linux server (a VPS). This page uses DigitalOcean. Other providers work the same way.

## Create the server

1. Create a DigitalOcean account with [this referral link](https://m.do.co/c/46b4d3d44795){ rel="sponsored nofollow" }. You get free credit, and the NadekoBot project gets credit after you spend $25.
2. Click **Create** > **Droplets**.
3. Select these options:

    | Option | Value |
    |---|---|
    | Region | The region nearest to you |
    | Image | Ubuntu 24.04 (LTS) x64 |
    | Droplet type | Basic |
    | CPU options | Regular |
    | Size | $6 per month (1 GiB memory, 1 vCPU, 25 GiB disk) |
    | Authentication Method | Password. Select SSH Key only if you already added a key to DigitalOcean. |

4. Click **Create Droplet**.
5. On the **Droplets** page, copy the IP address of the new droplet.

## Connect to the server

On Windows 10 or 11, open PowerShell. It has SSH, so you do not need PuTTY. On Linux or macOS, open Terminal. Then type this command with your IP address:

```sh
ssh root@YOUR_SERVER_IP
```

The first time, type `yes` to trust the server. If you selected a password, type it. The terminal does not show the password while you type.

When you see `root@...:~#`, you are on the server. Install the updates:

```sh
apt update && apt upgrade -y
```

If a window asks which services to restart, press ++enter++.

## Next step

Install the bot with the [CLI guide](cli-guide.md) or the [Docker guide](docker-guide.md).
