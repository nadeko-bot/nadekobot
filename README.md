# NadekoBot

[![CI/CD](https://github.com/nadeko-bot/nadekobot/actions/workflows/ci.yml/badge.svg)](https://github.com/nadeko-bot/nadekobot/actions/workflows/ci.yml)

NadekoBot is an open source Discord bot. It is written in C# and is built on .NET 8.

If you want to run your own instance of NadekoBot, please check out the [Self hosting Guides and Docs](https://docs.nadeko.bot).

If you have any questions, please visit our [Discord support server](https://discord.nadeko.bot).

## Installation

### Default option

You may want to consider using [upeko](https://github.com/nadeko-bot/upeko/releases) if you want to run bot on your PC.+

### Hosting on a linux server

If you want your bot to be online 24/7, you should [host it on a linux vps](https://docs.nadeko.bot/guides/linux-guide).

### Docker

There is an official Docker image for a [simple setup](https://docs.nadeko.bot/guides/docker-guide/)
Short version:
  ```sh
    docker run -d --name nadeko ghcr.io/nadeko-bot/nadekobot:v6 -e bot_token=YOUR_TOKEN_HERE -v "./data:/app/data" && docker logs -f --tail 500 nadeko
  ```

## Contributing to NadekoBot

We love your input! We want to make contributing to this project as easy as possible, whether it's:

- Reporting a bug
- Discussing the current state of the code
- Submitting a fix
- Proposing new features
- Becoming a maintainer

### Contribution

By submitting code, content, or materials via pull request or similar means ("Contribution"), you irrevocably assign all
intellectual property rights (including copyright and patents) to NadekoBot Repository Owner and affirm you either:

- (a) own the Contribution outright, or
- (b) it is licensed under compatible terms permitting unrestricted relicensing.

You grant the NadekoBot Repository Owner perpetual, worldwide rights to use, modify, distribute, and sublicense the
Contribution under AGPLv3, a commercial license, or any other terms without compensation.

These terms survive termination of this agreement.
