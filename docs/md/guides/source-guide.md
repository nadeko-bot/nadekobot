---
title: Build and run NadekoBot from source (Windows, Linux, macOS)
description: Build your own NadekoBot from source with the .NET 9 SDK on Windows, Linux or macOS. Clone the repository, add the token, run and update the bot.
---

# Source guide (Windows, Linux and macOS)

Build NadekoBot from its source code. Use this guide if you want to change the code. For a normal install, use the [desktop guide](desktop-guide.md), the [CLI guide](cli-guide.md) or the [Docker guide](docker-guide.md).

## Before you start

- [Create a Discord bot](../creds-guide.md) and copy its token.
- Install the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).
- Install [Git](https://git-scm.com/downloads).
- Install a text editor, for example [Visual Studio Code](https://code.visualstudio.com/).

## Install the bot

Open a terminal: PowerShell on Windows, Terminal on Linux and macOS. The commands are the same on all three systems.

1. Go to the folder for the bot, for example your home folder:

    ```sh
    cd ~
    ```

2. Download the source code and go to the bot project:

    ```sh
    git clone https://github.com/nadeko-bot/nadekobot -b v6 --depth 1
    cd nadekobot/src/NadekoBot
    ```

3. Make the credentials file:

    ```sh
    cp data/creds_example.yml data/creds.yml
    ```

4. Open `data/creds.yml` in your text editor. Put your token between the quotes of the `token` field, then save the file.
5. Build and start the bot:

    ```sh
    dotnet run -c Release
    ```

The bot comes online in your server. It uses the files in `src/NadekoBot/data`.

## Install the music tools

For music, the bot needs ffmpeg, yt-dlp and Deno in your `PATH`.

/// tab | Windows

```powershell
winget install Gyan.FFmpeg
winget install yt-dlp.yt-dlp
winget install DenoLand.Deno
```

Then close PowerShell and open it again. If music does not play, install the latest [Microsoft Visual C++ Redistributable (x64)](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist).

///
/// tab | Linux

```sh
sudo apt update
sudo apt install ffmpeg curl unzip
mkdir -p ~/.local/bin
curl -L -o ~/.local/bin/yt-dlp https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux
chmod +x ~/.local/bin/yt-dlp
curl -fsSL https://deno.land/install.sh | sh
```

On Fedora, use `sudo dnf install ffmpeg-free unzip`. On other systems, install ffmpeg with your package manager. Make sure that `~/.local/bin` is in your `PATH`.

The voice files in `data/lib` are for x64 only. On an ARM computer, music does not work.

///
/// tab | macOS

```sh
brew install ffmpeg yt-dlp deno
```

Music does not work on macOS yet, because the bot has no macOS voice files in `data/lib`.

///

## Update the bot

1. Stop the bot: press ++ctrl+c++, or type `.die` in Discord.
2. Make a copy of the `data` folder:

    ```sh
    cd ~/nadekobot/src/NadekoBot
    cp -r data ~/nadeko-data-backup
    ```

    If `~/nadeko-data-backup` exists, delete it first.

3. Download the new code:

    ```sh
    git pull --autostash
    ```

4. Start the bot:

    ```sh
    dotnet run -c Release
    ```

`--autostash` keeps the changes which the bot made in its config files. If Git shows `refusing to merge unrelated histories`, clone the bot again and copy your `data` folder from `~/nadeko-data-backup` into `src/NadekoBot/data`. If Git shows a different conflict, ask in the [NadekoBot Discord server](https://discord.nadeko.bot/).
