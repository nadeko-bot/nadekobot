---
title: Creating your own Discord bot
description: Create a Discord application for your own NadekoBot, copy the bot token, turn on the privileged intents and invite the bot to your server.
---

# Creating your own Discord bot

Before you install NadekoBot, you need a Discord bot account and its token. This page shows how to create the bot, get the token and invite the bot to your server.

## Create the bot and copy the token

1. Open the [Discord Developer Portal](https://discord.com/developers/applications) and log in.
2. Click **New Application**.
3. Type a name for the bot.
4. Accept the terms and click **Create**.
5. On the left, click **Bot**.
6. Click **Reset Token**, then click **Yes, do it!**.
7. Copy the token.
8. Put the token in the `token` field of `creds.yml`. Your install guide tells you where this file is.

!!! warning "Keep the token secret"
    The token is the password of your bot. A person with the token can control the bot. If someone sees your token, reset it immediately. A reset stops the old token, so put the new token in `creds.yml`.

## Turn on the privileged intents

1. On the **Bot** page, go to **Privileged Gateway Intents**.
2. Turn on all three intents:
    - **Presence Intent**
    - **Server Members Intent**
    - **Message Content Intent**
3. Click **Save Changes**.

Without these intents, some features do not work. Without **Message Content Intent**, the bot runs a command only when the message starts with a mention of the bot.

## Set yourself as the owner

The owner can use the owner-only commands, for example `.die` and `.config`. On the first start, the bot adds the owner of the Discord application to the `ownerIds` list. To add more owners, do these steps:

1. In Discord, open **User Settings** > **Advanced** and turn on **Developer Mode**.
2. Right-click your name and click **Copy User ID**.
3. Put the ID in the `ownerIds` list of `creds.yml`:

    ```yaml
    ownerIds:
      - 123456789012345678
    ```

## Invite the bot to your server

![The Discord Developer Portal: copy the Application ID, open the invite link and authorize the bot](https://cdn.nadeko.bot/tutorial/bot-invite-guide.gif)

1. On the left, click **General Information** and copy the **Application ID**.
2. In this link, replace `YOUR_APPLICATION_ID` with your Application ID:

    ```
    https://discord.com/oauth2/authorize?client_id=YOUR_APPLICATION_ID&scope=bot&permissions=66186303
    ```

3. Open the link in your browser.
4. Select your server and click **Authorize**.

The bot is now in your server. It stays offline until you start it.

## Next step

Install the bot with one of the guides:

- [Desktop guide (Upeko)](guides/desktop-guide.md) for Windows, Linux and macOS
- [CLI guide](guides/cli-guide.md) for Linux and macOS
- [Docker guide](guides/docker-guide.md)
- [Source guide](guides/source-guide.md)
