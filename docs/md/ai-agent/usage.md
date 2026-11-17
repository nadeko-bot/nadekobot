---
title: AI Agent Usage and Skills
description: Start the NadekoBot AI agent with a mention, .agent, a reply or the bot's name. Server admins add skills to change how it answers in their server.
---

# AI Agent Usage and Skills

The AI agent reads your request in plain language, runs the commands for it as you and replies with the result.

The bot owner turns on the AI agent and chooses who can use it. If the agent does not answer, ask the owner of the bot. The agent works only in a server channel, not in a direct message.

## Start the agent

| Method | Example |
|---|---|
| Mention the bot at the start of your message | `@Nadeko what is my balance?` |
| Use the `.agent` / `.ai` command | `.agent show the top 5 xp members` |
| Reply to an answer or a question of the agent, with the reply ping on | `and Bob?` |
| Start your message with the name of the bot | `Nadeko, warn @Bob for spam` |

The name of the bot is its nickname in the server, its display name or its username, in any letter case. The name must be at the start of your message. A space follows the name, or a sign such as `,` `:` `!` `?` and then a space. "Nadeko, roll a die" starts the agent. "hey Nadeko, roll a die", "Nadeko's music" and "I asked Nadeko" do not start it.

## Sessions

When the agent answers you or asks you a question, a session starts. In a session, each message which you send in that channel goes to the agent, without a mention:

```text
@Nadeko how many flowers does Alice have?
  Alice has 5,000 flowers.
and Bob?
  Bob has 1,200 flowers.  [End session]
```

The newest answer of the session has an **End session** button. While the session is open, the agent reads each message which you send in that channel. Only you can use the button on your session.

The session ends when one of these things occurs:

- You click **End session**.
- You say goodbye or thanks, and the agent ends the session.
- You type `.agentcancel` / `.aicancel` / `.aic`.
- 2 minutes pass after the last answer of the agent, or after your last request in the session. The bot owner can change this time.

Then the bot removes the button within a few seconds. **End session** and `.agentcancel` end your sessions in all channels. A request which only runs a command, for example `@Nadeko roll a die`, does not start a session.

To stop an answer which is running, type `.agentcancel` / `.aicancel` / `.aic`, or click **End session** on the last answer.

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

- The agent keeps the conversation only in memory, not in the database. It removes the recent messages of a channel from memory after 30 minutes without activity, and when the bot restarts.
- The agent reads your private data (for example your reminders) only for you. It cannot read the private data of another member.
- The bot removes hidden tags and control characters from names and messages before the AI model sees them.

## Related pages

- [AI agent overview](https://nadeko.bot/features/ai-agent)
- [Owner setup](owner-setup.md)
