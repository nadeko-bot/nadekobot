---
title: Change NadekoBot settings with .config and the config files
description: Change the settings of your self-hosted NadekoBot in Discord with .config, or edit the files in the data folder and reload them with .configreload.
---

# Bot settings (.config)

The bot keeps its settings in `.yml` files in the `data` folder, for example `data/bot.yml` and `data/gambling.yml`. You can change most settings in Discord with `.config`. Only the bot owner can use `.config` and `.configreload`.

These settings apply to the whole bot, not to one server. The [database](database.md) keeps the server settings.

## Change a setting in Discord

| Command | What it does |
|---|---|
| `.config` | Shows the list of configs. |
| `.config <config>` | Shows the settings of the config and their values. |
| `.config <config> <setting>` | Shows the value and the description of the setting. |
| `.config <config> <setting> <value>` | Sets the setting to the new value. |

For example:

```text
.config bot
.config bot color.ok
.config bot color.ok ff0000
```

`.conf` is a short form of `.config`. You can type only the start of the config name, for example `.config gamb` for `gambling`. Type enough letters to match only one config: `ga` matches both `gambling` and `games`. Type `.h .config` for help in Discord.

The bot saves the new value in the file at once. You do not have to restart the bot.

## Edit a config file

Some settings are only in the files, not in `.config`. To change them, edit the file:

1. Open the file in the `data` folder with a text editor. The [database page](database.md#find-the-database) shows where the `data` folder is for each install. The table below shows the file of each config.
2. Change the value and save the file.
3. Type `.configreload <config>` in Discord, for example `.configreload gambling`. You can also restart the bot.

`.creload`, `.confreload` and `.crel` are short forms of `.configreload`.

!!! warning "Reload before you set a value with .config"
    Until you reload, the bot uses the old values. If you set a value with `.config` on that config before you reload, the bot writes all its old values to the file, and your edits are lost.

The files are YAML. Keep the indentation and the quotes as they are. If the file has a mistake, the reload fails, the bot keeps the old values and writes the error in its log. The bot does not start with a broken file. Make a copy of the file before you edit it.

A wrong setting name does not stop the reload. The bot ignores a name which it does not know, and the next `.config` change removes it from the file. Check the names with `.config <config>`.

| Config | File |
|---|---|
| `aiagent` | `data/ai-agent.yml` |
| `bot` | `data/bot.yml` |
| `fishing` | `data/fish.yml` |
| `gambling` | `data/gambling.yml` |
| `games` | `data/games.yml` |
| `images` | `data/images.yml` |
| `music` | `data/music.yml` |
| `patron` | `data/patron.yml` |
| `searches` | `data/searches.yml` |
| `utility` | `data/utility.yml` |
| `waifu` | `data/waifu.yml` |
| `xp` | `data/xp.yml` |

`.config` shows the configs of your bot version. A new version can add configs.

## creds.yml

`data/creds.yml` keeps the token, the owner IDs and the API keys. It is not a config of `.config`. After you edit it, type `.credsreload` or restart the bot. A new token needs a restart. With Docker, `bot_token` in the container settings replaces the token of `creds.yml` (see the [Docker guide](docker-guide.md#change-the-settings)).
