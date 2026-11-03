---
title: NadekoBot AI agent - talk to the bot in plain language
description: The NadekoBot AI agent reads your message, runs the right bot commands for you and replies with the result. Learn what it does and who controls it.
---

# AI agent

The NadekoBot AI agent lets you talk to the bot in plain language. The agent reads your message and finds the commands it needs. It runs them as you and replies with the result.

Example: you write `@Nadeko show my XP and give 100 flowers to @Alice`. The agent gets your XP, runs `.give 100 @Alice` and tells you what it did.

## What the agent can do

- **Read data.** The agent reads balances, XP, settings and other data through read-only tools.
- **Run commands.** The agent runs normal bot commands as you. It can do only what you can do with the same commands. It never runs owner-only commands.
- **Send messages.** The agent posts messages in the channels where you can send messages. It edits only messages of the bot.

An alias, a scheduled command or a command which the agent runs never starts the agent.

## Who controls the agent

| Who | What they control | Where |
|---|---|---|
| Bot owner | The AI provider and key, the model, who can use the agent, and the base prompts | `creds.yml`, `data/ai-agent.yml`, `.agentwhitelist`, `.aiprompt` |
| Members with the Administrator permission | Extra instructions for their server or one channel (skills) | `.agentskilladd`, `.agentchannelskilladd` |
| Members | Their own requests | `.agent`, a mention, a reply, the name of the bot |

Skills cannot give a member more permissions. A skill only changes the style and the topics of the answers.

## Next steps

- [Owner setup](owner-setup.md): turn on the agent on your own bot.
- [Usage and skills](usage.md): start the agent and add skills.
