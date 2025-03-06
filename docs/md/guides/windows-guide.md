## Setting Up NadekoBot on Windows With the Updater

--8<-- "md/creds-guide.md"

#### Prerequisites

- Windows 10 or later (64-bit)

#### Setup

- Download and run the [upeko][Updater].
 ![Create a new bot](../assets/upeko-1.png "Create a new bot")
- Click the plus button to add a new bot
![Open bot page](../assets/upeko-2.png "Open bot page")
- If you want to use the music module, click on **`Install`** next to `ffmpeg` and `yt-dlp` at the top
- Click on the newly created bot
 ![Bot Setup](../assets/upeko-3.png "Bot Setup")
- Click on **DOWNLOAD** at the lower right
 ![Download](../assets/upeko-4.png "Download")
 ![Creds](../assets/upeko-5.png "Edit creds")
- When installation is finished, click on **CREDS** (1) above the **RUN** (3) button on the lower left
- 2 simply opens your bot's data folder.
- Paste in your BOT TOKEN previously obtained

#### Starting the bot

- Either click on **`RUN`** button in the updater or run the bot via its desktop shortcut.

#### Updating Nadeko

- Make sure Nadeko is closed and not running
  (Run `.die` in a connected server to make sure).
- Make sure you don't have `data` folder, bot folder, or any other bot file open in any program, as the updater will fail to replace your version
- Run `upeko` if not already running
- Click on your bot
- Click on **`Check for updates`**
- If updates are available, you will be able to click on the Update button
- Click `Update`
- Click `RUN` after it's done