## Creating your own Discord bot

This guide will show you how to create your own discord bot, invite it to your server, and obtain the credentials needed to run it.

1. Go to [the Discord developer application page][DiscordApp].
1. Log in with your Discord account.
1. Click **New Application**.
1. Fill out the `Name` field however you like, accept the terms, and confirm.
1. Go to the **Bot** tab on the left sidebar.
1. Click on the `Add a Bot` button and confirm that you do want to add a bot to this app.
1. **Optional:** Add bot's avatar and description.
1. Copy your Token to `creds.yml` as shown above.
1. Scroll down to the **`Privileged Gateway Intents`** section
    - You MUST enable the following:
         - **PRESENCE INTENT**
         - **SERVER MEMBERS INTENT**
         - **MESSAGE CONTENT INTENT**

#### Inviting your bot to your server

![Invite the bot to your server](https://cdn.nadeko.bot/tutorial/bot-invite-guide.gif)

- On the **General Information** tab, copy your `Application ID` from your [applications page][DiscordApp].
- Replace the `YOUR_CLIENT_ID_HERE` in this link:
  `https://discordapp.com/oauth2/authorize?client_id=YOUR_CLIENT_ID_HERE&scope=bot&permissions=66186303` with your `Client ID`
- The link should now look something like this:
  `https://discordapp.com/oauth2/authorize?client_id=123123123123&scope=bot&permissions=66186303`
- Access that newly created link, pick your Discord server, click `Authorize` and confirm with the captcha at the end
- The bot should now be in your server


[DiscordApp]: https://discordapp.com/developers/applications/me