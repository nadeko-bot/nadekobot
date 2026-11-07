---
title: Install and host your own Discord bot
description: Host your own NadekoBot, the open source Discord bot. Pick a guide for Upeko, the CLI installer, Docker or the source code.
---

# NadekoBot docs

NadekoBot is an open source Discord bot for moderation, games, XP and leveling, an economy with Nadeko Flowers, and hundreds of utility commands.

## Use the public bot

Add the public NadekoBot to your server. You do not install anything.

[:material-plus: Add NadekoBot to your server][invite]{ .md-button .md-button--primary }
[:material-format-list-text: Commands][commands]{ .md-button }

## Host your own bot

Each option below needs your own Discord bot. First, [create a Discord bot](creds-guide.md) and copy its token. Then pick a guide:

| Your setup | Guide |
|---|---|
| No server and no setup (paid hosting, free trial) | [NadeKloud](https://cloud.nadeko.bot/?utm_source=docs&utm_content=home-table) |
| Use a window app on Windows, Linux or macOS | [Desktop guide (Upeko)][desktop-guide] |
| Use the terminal on Linux or macOS | [CLI guide][cli-guide] |
| Run the bot in a Docker container | [Docker guide][docker-guide] |
| Build the bot from its source code | [Source guide][source-guide] |

A server keeps the bot online all day. [Get a Linux server](guides/linux-server.md), then use the CLI guide or the Docker guide.

<div class="nk-panel">
<div class="nk-panel-head">
<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 18.5h10.5a4 4 0 0 0 .6-7.96A6 6 0 0 0 6.6 9.1 4.75 4.75 0 0 0 7 18.5Z" fill="currentColor" fill-opacity=".2"/></svg>
<span class="nk-panel-title"><span class="nk-ink">nade</span><span class="nk-pink">kloud</span></span>
<span class="nk-panel-aside">Hosted NadekoBot</span>
</div>
<p>Want your own bot without the setup? <strong>NadeKloud</strong> is paid hosting for NadekoBot, with a free trial. It runs your bot for you, and you set it up in a web dashboard.</p>
<a class="md-button" href="https://cloud.nadeko.bot/?utm_source=docs&amp;utm_content=home">Try NadeKloud</a>
</div>

## Features

- [AI agent](https://nadeko.bot/features/ai-agent): members ask the bot for things in plain language. To turn it on for your own bot, read [Owner setup](ai-agent/owner-setup.md).
- [Waifu system](https://nadeko.bot/features/waifus): an economy game with waifus, fans and managers.
- [Medusa plugins](medusa/getting-started.md): add your own commands to a self-hosted bot.

## Get help

- Ask in the [NadekoBot Discord server][discord-server].
- Report a bug on [GitHub][issues]. If you are not sure that it is a bug, ask in the Discord server first.
- NadekoBot is [open source][github]. [Donations][donate] keep the project alive.

[invite]: https://invite.nadeko.bot/
[commands]: https://nadeko.bot/commands/
[desktop-guide]: guides/desktop-guide.md
[cli-guide]: guides/cli-guide.md
[docker-guide]: guides/docker-guide.md
[source-guide]: guides/source-guide.md
[discord-server]: https://discord.nadeko.bot/
[github]: https://github.com/nadeko-bot/nadekobot
[issues]: https://github.com/nadeko-bot/nadekobot/issues
[donate]: donate.md
