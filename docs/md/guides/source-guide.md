# Setting Up NadekoBot on Windows from source

### Prerequisites

- Windows 10 or later (64-bit)
- [.net 8 sdk](https://dotnet.microsoft.com/download/dotnet/8.0)
- If you want nadeko to play music: [Visual C++ 2010 (x86)] and [Visual C++ 2017 (x64)] (both are required, you may install them later)
- [git](https://git-scm.com/downloads) - needed to clone the repository (you can also download the zip manually and extract it, but this guide assumes you're using git)
- **Optional** Any code editor, for example [Visual Studio Code](https://code.visualstudio.com/Download)
    - You'll need to at least modify creds.yml, notepad is inadequate

---

??? note "Creating a Discord Bot & Getting Credentials"
    --8<-- "md/creds-guide.md"

---

## Installation Instructions

Open PowerShell (press windows button on your keyboard and type powershell, it should show up; alternatively, right click the start menu and select Windows PowerShell), and

1. Navigate to the location where you want to install the bot
    - for example, type `cd ~/Desktop/` and press enter
2. `git clone https://github.com/nadeko-bot/nadekobot -b v6 --depth 1`
3. `cd nadekobot/src/NadekoBot`
4. `dotnet build -c Release`
5. `cp data/creds_example.yml data/creds.yml`
6. "You're done installing, you may now proceed to set up your bot's credentials by following the [#creds-guide]
    - Once done, come back here and run the last command
6. Run the bot `dotnet NadekoBot.dll`
7. 🎉 Enjoy

## Update Instructions

Open PowerShell as described above and run the following commands:

1. Stop the bot
    - ⚠️ Make sure you don't have your database, credentials or any other nadekobot folder open in some application, this might prevent some of the steps from executing successfully
2. Navigate to your bot's folder, example:
    - `cd ~/Desktop/nadekobot`
3. Pull the new version, and make sure you're on the v6 branch
    - `git pull`
    - ⚠️ IF this fails, you may want to `git stash` or remove your code changes if you don't know how to resolve merge conflicts
4. **Backup** old output in case your data is overwritten
    - `cp -r -fo output/ output-old`
5. Build the bot again
    - `dotnet run -c Release src/NadekoBot/`
6. Copy old data, and new strings
    - `cp -r -fo .\output-old\data\ .\output\`
7. Run the bot
    - `cd output`
    - `dotnet NadekoBot.dll`
8. 🎉 Enjoy

## Music Prerequisites

In order to use music commands, you need ffmpeg and yt-dlp installed.

- [ffmpeg]
- [yt-dlp]
    - Click to download the `yt-dlp.exe` file, then move `yt-dlp.exe` to a path that's in your PATH environment variable. If you don't know what that is, just move the `yt-dlp.exe` file to your nadekobot's output folder.

[.net]: https://dotnet.microsoft.com/download/dotnet/8.0
[ffmpeg]: https://github.com/GyanD/codexffmpeg/releases/latest
[yt-dlp]: https://github.com/yt-dlp/yt-dlp/releases/latest
