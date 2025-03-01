--8<-- "md/creds-guide.md"

## Setting Up NadekoBot on Windows from source

1. Prerequisites

- Windows 10 or later (64-bit)
- [.net 8 sdk](https://dotnet.microsoft.com/download/dotnet/8.0)
- If you want nadeko to play music: [Visual C++ 2010 (x86)] and [Visual C++ 2017 (x64)] (both are required, you may install them later)
- [git](https://git-scm.com/downloads) - needed to clone the repository (you can also download the zip manually and extract it, but this guide assumes you're using git)
- **Optional** Any code editor, for example [Visual Studio Code](https://code.visualstudio.com/Download)
    - You'll need to at least modify creds.yml, notepad is inadequate


##### Installation Instructions

Open PowerShell (press windows button on your keyboard and type powershell, it should show up; alternatively, right click the start menu and select Windows PowerShell), and


0. Navigate to the location where you want to install the bot
    - for example, type `cd ~/Desktop/` and press enter
1. `git clone https://github.com/nadeko-bot/nadekobot -b v6 --depth 1`
1. `cd nadekobot/src/NadekoBot`
1. `dotnet build -c Release`
1. `cp data/creds_example.yml data/creds.yml`
1. "You're done installing, you may now proceed to set up your bot's credentials by following the [#creds-guide]
    - Once done, come back here and run the last command
1. Run the bot `dotnet NadekoBot.dll`
1. 🎉 Enjoy

##### Update Instructions

Open PowerShell as described above and run the following commands:

1. Stop the bot
  - ⚠️ Make sure you don't have your database, credentials or any other nadekobot folder open in some application, this might prevent some of the steps from executing succesfully
1. Navigate to your bot's folder, example:
    - `cd ~/Desktop/nadekobot`
1. Pull the new version, and make sure you're on the v6 branch
    - `git pull`
    - ⚠️ If this fails, you may want to stash or remove your code changes if you don't know how to resolve merge conflicts
1. **Backup** old output in case your data is overwritten
    - `cp -r -fo output/ output-old`
1. Build the bot again
    - `dotnet run -c Release src/NadekoBot/`
1. Copy old data, and new strings
    - `cp -r -fo .\output-old\data\ .\output\`
1. Run the bot
    - `cd output`
    - `dotnet NadekoBot.dll`

🎉 Enjoy

#### Music prerequisites
In order to use music commands, you need ffmpeg and yt-dlp installed.
- [ffmpeg-32bit] | [ffmpeg-64bit] - Download the **appropriate version** for your system (32 bit if you're running a 32 bit OS, or 64 if you're running a 64bit OS). Unzip it, and move `ffmpeg.exe` to a path that's in your PATH environment variable. If you don't know what that is, just move the `ffmpeg.exe` file to `NadekoBot/output`.
- [youtube-dlp] - Click to download the `yt-dlp.exe` file, then move `yt-dlp.exe` to a path that's in your PATH environment variable. If you don't know what that is, just move the `yt-dlp.exe` file to `NadekoBot/system`.

[Updater]: https://dl.nadeko.bot/v3/
[Notepad++]: https://notepad-plus-plus.org/
[.net]: https://dotnet.microsoft.com/download/dotnet/8.0
[Redis]: https://github.com/MicrosoftArchive/redis/releases/download/win-3.0.504/Redis-x64-3.0.504.msi
[Visual C++ 2010 (x86)]: https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe
[Visual C++ 2017 (x64)]: https://aka.ms/vs/15/release/vc_redist.x64.exe
[ffmpeg-32bit]: https://cdn.nadeko.bot/dl/ffmpeg-32.zip
[ffmpeg-64bit]: https://cdn.nadeko.bot/dl/ffmpeg-64.zip
[youtube-dlp]: https://github.com/yt-dlp/yt-dlp/releases
