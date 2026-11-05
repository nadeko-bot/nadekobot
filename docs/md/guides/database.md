---
title: Find, open and edit the NadekoBot database
description: Where your self-hosted NadekoBot keeps NadekoBot.db, how to open it with DB Browser for SQLite or sqlite3, and how to change it safely with a backup.
---

# Database

NadekoBot keeps all server settings and member data in one SQLite file, `NadekoBot.db`. This page is for the owner of a self-hosted bot.

You do not have to make the file. The bot creates it on the first start.

## Find the database

The database is in the bot's `data` folder:

| Install | Database file |
|---|---|
| [Desktop guide (Upeko)](desktop-guide.md) | `data/NadekoBot.db` in the folder of **Install Path** on the bot page. Click **Data Folder** in **Shortcuts** to open it. On Windows, the default folder is `Documents\upeko\bot-xxxxx`. |
| [CLI guide](cli-guide.md) | `nadeko/data/NadekoBot.db` in your home folder |
| [Docker guide](docker-guide.md) | `data/NadekoBot.db` in the bot's folder, for example `~/nadeko/data/NadekoBot.db` |
| [Source guide](source-guide.md) | `nadekobot/src/NadekoBot/data/NadekoBot.db` |

The bot reads `data` in the folder where it starts. If you start the built program from `bin/Release/net9.0` (for example from an IDE), the database is in `bin/Release/net9.0/data`. A build copies the files of `src/NadekoBot/data` into that folder and can replace the database there. Always start the bot from the same folder, and edit the database in that folder.

While the bot runs, you can also see the files `NadekoBot.db-wal` and `NadekoBot.db-shm`. They are part of the database. Keep them together with `NadekoBot.db`.

## Get a database tool

/// tab | Windows and macOS

Install [DB Browser for SQLite](https://sqlitebrowser.org/dl/).

///
/// tab | Linux

Install the `sqlite3` program:

```sh
sudo apt install sqlite3
```

On Fedora, use `sudo dnf install sqlite`. You can also use DB Browser for SQLite if your computer has a desktop.

///

## Edit the database

!!! warning "Stop the bot and make a backup first"
    If you change the file while the bot runs, you can lose the change or damage the file. A wrong change can break the bot. Always stop the bot and copy the files before you edit them.

1. Stop the bot. Type `.die` in Discord, or click **Stop Instance** in Upeko. With systemd, type `sudo systemctl stop nadeko`. With Docker, type `docker stop nadeko`.
2. Copy `NadekoBot.db` to a safe place. If `NadekoBot.db-wal` and `NadekoBot.db-shm` are in the folder, copy them too.
3. Edit the database:

    /// tab | DB Browser for SQLite

    1. Click **Open Database** and select `NadekoBot.db`.
    2. Open the **Browse Data** tab.
    3. In the **Table** list, select the table.
    4. Click the cell which you want to change.
    5. Type the new value in the **Edit Database Cell** pane on the right.
    6. Click **Apply**.
    7. Click **Write Changes**.
    8. Close DB Browser for SQLite.

    ///
    /// tab | sqlite3

    1. Open the database. For example, with the CLI install:

        ```sh
        sqlite3 ~/nadeko/data/NadekoBot.db
        ```

        With Docker, the bot writes its files as `root`. Use `sudo sqlite3 ~/nadeko/data/NadekoBot.db`.

    2. Type `.tables` to show the tables, and `.schema DiscordUser` to show the columns of a table.
    3. Type your SQL. End each statement with `;`. For example, to give a member 1000 currency:

        ```sql
        UPDATE DiscordUser SET CurrencyAmount = CurrencyAmount + 1000 WHERE UserId = 123456789012345678;
        ```

    4. Type `.quit`.

    ///

4. Start the bot.

If the bot does not start after your change, restore the backup: stop the bot, move `NadekoBot.db`, `NadekoBot.db-wal` and `NadekoBot.db-shm` to a different folder, then copy your backup files back.

## Use SQL from Discord

The owner can also read and change the database in Discord, while the bot runs:

- `.sqlselect <sql>` shows the result of a query which only reads. It works only in DMs with the bot.
- `.sqlselectcsv <sql>` sends the result as a CSV file. It works only in DMs with the bot.
- `.sqlexec <sql>` runs SQL which changes the database. The bot asks you to confirm first.

For example: `.sqlselect SELECT UserId, CurrencyAmount FROM DiscordUser ORDER BY CurrencyAmount DESC LIMIT 10`

Many changes have a command, for example `.award` for currency. Use the command when there is one.
