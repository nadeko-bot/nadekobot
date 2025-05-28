## Creating your own Discord bot

This guide will show you how to create your own discord bot, invite it to your server, and obtain the credentials needed to run it.

1. Go to [the Discord developer application page][DiscordApp].
2. Log in with your Discord account.
3. Click **New Application**.
4. Fill out the **Name** field however you like, accept the terms, and confirm.
5. Go to the **Bot** tab on the left sidebar.
6. **Optional:** Add bot's avatar and description.
7. Click the **Reset Token** button, select **Yes, do it!** to confirm token reset, then copy the token that is revealed.

    !!! warning "IMPORTANT"
        **Keep your token safe**: The token is like a password for your bot. Anyone with this token can control your bot. If you accidentally share your token, reset it immediately to prevent unauthorized access.

8. Paste your token into the `token` field within `creds.yml`.
9. Scroll down to the **`Privileged Gateway Intents`** section.
    - You MUST enable the following:
        - **PRESENCE INTENT**
        - **SERVER MEMBERS INTENT**
        - **MESSAGE CONTENT INTENT**

### Inviting your bot to your server

![Invite the bot to your server](https://cdn.nadeko.bot/tutorial/bot-invite-guide.gif)

- From the **General Information** tab, copy your `Application ID`.
- Replace the `YOUR_CLIENT_ID_HERE` in the following link, with your `Client ID`:
  `https://discordapp.com/oauth2/authorize?client_id=YOUR_CLIENT_ID_HERE&scope=bot&permissions=66186303`
    - The link should now look something like this: `https://discordapp.com/oauth2/authorize?client_id=123123123123&scope=bot&permissions=66186303`
- Access that newly created link, pick your Discord server, click `Authorize` and confirm with the captcha at the end.
- The bot should now be in your server!


[DiscordApp]: https://discordapp.com/developers/applications/me
