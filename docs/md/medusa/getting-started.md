---
title: NadekoBot medusa plugins - add your own commands
description: Medusae are plugins for a self-hosted NadekoBot. They add commands and features without a change to the bot's code, and you load them while the bot runs.
---

# Medusa plugins

A medusa is a plugin for a self-hosted NadekoBot. It adds commands and features without a change to the bot's code. You can load, unload and update a medusa while the bot runs.

A bot on NadeKloud cannot load medusae.

## Manage medusae

Put each medusa in its own folder in `data/medusae`. The folder and the `.dll` file must have the same name, for example `data/medusae/mymedusa/mymedusa.dll`. Then use these owner-only commands:

| Command | What it does |
|---|---|
| `.medusalist` / `.melist` | Shows the folders in `data/medusae`, and which of them are loaded |
| `.medusaload <name>` / `.meload` | Loads a medusa |
| `.medusaunload <name>` / `.meunload` | Unloads a medusa |
| `.medusareload <name>` / `.mereload` | Unloads a medusa and loads it again |
| `.medusainfo <name>` / `.meinfo` | Shows the description, the sneks and the number of commands of a medusa |

The bot loads the same medusae again after a restart. It keeps the list in the `loaded` list of `data/medusae/medusa.yml`.

## Use only medusae which you trust

!!! danger
    A medusa runs with all the rights of the bot. A bad medusa can steal your bot token, and it can read and delete the files on the computer.

- Load only medusae which you wrote, or which come from a person you trust completely.
- Run the bot on a server which runs nothing else.

To find medusae from other people, ask in the `#dev-and-modding` channel of the [NadekoBot Discord server](https://discord.nadeko.bot/).

## Make a medusa

You write a medusa in C#. You need some knowledge of C# and .NET. Read [Create a medusa](creating-a-medusa.md).
