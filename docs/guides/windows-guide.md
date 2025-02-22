## Setting Up NadekoBot on Windows With the Updater

| Table of Contents|
| :-|
| [Prerequisites](#prerequisites) |
| [Setup](#setup) |
| [Starting the Bot](#starting-the-bot) |
| [Updating Nadeko](#updating-nadeko) |
| [Manually Installing the Prerequisites from the Updater](#music-prerequisites) |

*Note: If you want to make changes to Nadeko's source code, please follow the [From Source](#windows-from-source) guide instead.*

--8<-- "docs/creds-guide.md"

#### Prerequisites

- Windows 10 or later (64-bit)

**Optional**

- [Visual Studio Code](https://code.visualstudio.com/Download) (Highly suggested if you plan on editing files)
- [Visual C++ 2010 (x86)] and [Visual C++ 2017 (x64)] (both are required if you want Nadeko to play music - restart Windows after installation)

#### Setup

- Download and run the [NadekoBot v3 Updater][Updater].
- Click on the + at the top left to create a new bot.
 ![NadekoBot Updater](https://i.imgur.com/FmR7F7o.png "NadekoBot Updater")
- Give your bot a name and then click **`Go to setup`** at the lower right.
 ![Create a new bot](https://i.imgur.com/JxtRk9e.png "Create a new bot")
- Click on **`DOWNLOAD`** at the lower right
 ![Bot Setup](https://i.imgur.com/HqAl36p.png "Bot Setup")
- If you want to use the music module, click on **`Install`** next to **`FFMPEG`** and **`Youtube-DLP`**.
- If any dependencies fail to install, you can temporarily disable your Windows Defender/AV until you install them. If you don't want to, then read [the last section of this guide](#Manual-Prerequisite-Installation).
- When installation is finished, click on **`CREDS`** to the left of **`RUN`** at the lower right.
- Follow the guide on how to [Set up the creds.yml](../../creds-guide) file.

#### Starting the bot

- Either click on **`RUN`** button in the updater or run the bot via its desktop shortcut.

#### Updating Nadeko

- Make sure Nadeko is closed and not running
  (Run `.die` in a connected server to ensure it's not running).
- Open NadekoBot Updater
- Click on your bot at the upper left (looks like a spy).
- Click on **`Check for updates`**.
- If updates are available, you will be able to click on the Update button.
- Launch the bot
- You've updated and are running again, easy as that!


#### Music prerequisites
In order to use music commands, you need ffmpeg and yt-dlp installed.
- [ffmpeg-32bit] | [ffmpeg-64bit] - Download the **appropriate version** for your system (32 bit if you're running a 32 bit OS, or 64 if you're running a 64bit OS). Unzip it, and move `ffmpeg.exe` to a path that's in your PATH environment variable. If you don't know what that is, just move the `ffmpeg.exe` file to `NadekoBot/output`.
- [youtube-dlp] - Click to download the `yt-dlp.exe` file, then move `yt-dlp.exe` to a path that's in your PATH environment variable. If you don't know what that is, just move the `yt-dlp.exe` file to `NadekoBot/system`.

[Updater]: https://dl.nadeko.bot/v3/
[Notepad++]: https://notepad-plus-plus.org/
[.net]: https://dotnet.microsoft.com/download/dotnet/5.0
[Redis]: https://github.com/MicrosoftArchive/redis/releases/download/win-3.0.504/Redis-x64-3.0.504.msi
[Visual C++ 2010 (x86)]: https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe
[Visual C++ 2017 (x64)]: https://aka.ms/vs/15/release/vc_redist.x64.exe
[ffmpeg-32bit]: https://cdn.nadeko.bot/dl/ffmpeg-32.zip
[ffmpeg-64bit]: https://cdn.nadeko.bot/dl/ffmpeg-64.zip
[youtube-dlp]: https://github.com/yt-dlp/yt-dlp/releases
