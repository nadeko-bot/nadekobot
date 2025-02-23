--8<-- "docs/creds-guide.md"

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
1. `git clone https://github.com/nadeko-bot/nadekobot -b v5 --depth 1`
2. `cd nadekobot`
3. `dotnet publish -c Release -o output/ src/NadekoBot/`
4. `cd output`
5. `cp creds_example.yml creds.yml`
6. "You're done installing, you may now proceed to set up your bot's credentials by following the [#creds-guide]
    - Once done, come back here and run the last command
8. Run the bot `dotnet NadekoBot.dll`
9. 🎉 Enjoy

##### Update Instructions

Open PowerShell as described above and run the following commands:

1. Stop the bot
  - ⚠️ Make sure you don't have your database, credentials or any other nadekobot folder open in some application, this might prevent some of the steps from executing succesfully
2. Navigate to your bot's folder, example:
    - `cd ~/Desktop/nadekobot`
3. Pull the new version, and make sure you're on the v5 branch
    - *⚠️ If you're on v4, you must run these commands, if not, you may skip them.*
        - `git remote set-branches origin '*'`
        - `git fetch -v --depth=1`
        - `git checkout v5`
    - `git pull`
    - ⚠️ If this fails, you may want to stash or remove your code changes if you don't know how to resolve merge conflicts
4. **Backup** old output in case your data is overwritten
    - `cp -r -fo output/ output-old`
5. Build the bot again
    - `dotnet publish -c Release -o output/ src/NadekoBot/`
6. Remove old strings and aliases to avoid overwriting the updated versions of those files
    - ⚠ If you've modified said files, back them up instead
    - `rm output-old/data/aliases.yml`
    - `rm -r output-old/data/strings`
7. Copy old data, and new strings
    - `cp -Recurse -Force .\output-old\data\ .\output\`
    - `cp -Recurse -Force src/NadekoBot/data/strings/ output/data/`
8. Copy creds.yml
    - `cp output-old/creds.yml output/`
9. Run the bot
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
