---
title: Use the NadekoBot AI agent and add skills
description: Start the NadekoBot AI agent with a mention, .agent, a reply or the bot's name. Server admins add skills to change how it answers in their server.
---

# Usage and skills

The bot owner turns on the AI agent and chooses who can use it. If the agent does not answer, ask the owner of the bot. The agent works only in a server channel, not in a direct message.

## Start the agent

| Method | Example |
|---|---|
| Mention the bot at the start of your message | `@Nadeko what is my balance?` |
| Use the `.agent` / `.ai` command | `.agent show the top 5 xp members` |
| Reply to an answer of the agent, with the reply ping on | `and Bob?` |
| Say the name of the bot | `hey Nadeko, warn @Bob for spam` |

A mention and `.agent` start the agent for each request. A reply and the name of the bot start it only when your message looks like a request. "lol" or "thanks" does not start it.

After the agent answers, you can continue in the same channel without a mention. This works for 2 minutes (the bot owner can change this time). In this time, each message which you send in that channel goes to the agent:

```text
@Nadeko how many flowers does Alice have?
  Alice has 5,000 flowers.
and Bob?
  Bob has 1,200 flowers.
```

To stop an answer which is running, type `.agentcancel` / `.aic`.

## Skills

A skill is an instruction which server admins add for the agent. A server skill works in all channels. A channel skill works only in its channel. A skill changes the style and the topics of the answers. It cannot give a member more permissions.

- A server can have up to 10 skills. Server skills and channel skills count together.
- A skill name can have up to 50 characters. An instruction can have up to 2,000 characters.
- You need the **Administrator** permission.

### Server skills

A server skill works in all channels of the server.

| Command | What it does |
|---|---|
| `.agentskilladd <name> <instruction>` / `.aisa` | Adds a skill |
| `.agentskillremove <name>` / `.aisr` | Removes a skill |
| `.agentskilltoggle <name>` / `.aist` | Turns a skill on or off |
| `.agentskilllist` / `.aisl` | Shows the skills, with buttons to turn them on or off |

Example:

```text
.agentskilladd tone Reply in a calm, formal tone.
.agentskilladd prices Do not tell prices. Tell members to read #pricing.
```

### Channel skills

A channel skill works only in the channel where you add it. Use it for a channel with its own rules, for example a formal `#support` channel.

| Command | What it does |
|---|---|
| `.agentchannelskilladd <name> <instruction>` / `.aicsa` | Adds a skill to this channel |
| `.agentchannelskillremove <name>` / `.aicsr` | Removes a skill from this channel |
| `.agentchannelskilltoggle <name>` / `.aicst` | Turns a channel skill on or off |
| `.agentchannelskilllist` / `.aicsl` | Shows the skills of this channel |

## Privacy

- The agent keeps the conversation only in memory, not in the database. It forgets the recent messages of a channel after 30 minutes without activity, and after a restart of the bot.
- The agent reads your private data (for example your reminders) only for you. It cannot read the private data of another member.
- The bot removes hidden tags and control characters from names and messages before the AI model sees them.

## Related pages

- [AI agent overview](overview.md)
- [Owner setup](owner-setup.md)
