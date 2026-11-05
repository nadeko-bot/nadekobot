---
title: Set up the NadekoBot AI agent on your own bot
description: Turn on the NadekoBot AI agent on a self-hosted bot. Add an OpenAI-compatible API key, pick a model, choose who can use it and edit the prompts.
---

# Owner setup

This page is for the owner of a self-hosted NadekoBot. To use the agent on a bot which someone else hosts, read [Usage and skills](usage.md).

## Pick an AI provider

The agent needs an OpenAI-compatible chat API with tool calling (function calling). These providers work:

- OpenAI
- OpenRouter, which can send a request to other models if one fails
- A local server (vLLM, llama.cpp) with an OpenAI-compatible API and a model with tool calling

A provider or model without tool calling refuses the requests of the agent, and the agent does not answer.

## Turn on the agent

1. Put your API key in `aiApiKey` in `data/creds.yml`, then restart the bot.
2. Open `data/ai-agent.yml` and set these fields:

    ```yaml
    enabled: true
    # Without /v1/chat/completions at the end
    apiUrl: https://openrouter.ai/api
    modelName: openai/gpt-5.4
    ```

3. Type `.configreload aiagent` in Discord, or restart the bot.

The bot needs the **Message Content** intent (see [Create a Discord bot](../creds-guide.md#turn-on-the-privileged-intents)). Without it, only a mention of the bot starts the agent. The name trigger, the follow-up time and the channel memory then do not work.

You can also change most settings in Discord with `.config aiagent <setting> <value>`. For example: `.config aiagent model openai/gpt-5.4`. Type `.config aiagent` to see all settings.

| Setting | Default | What it does |
|---|---|---|
| `enabled` | `false` | Turns the agent on or off |
| `model` | `gpt-5.4` | The model name |
| `reasoning` | `low` | The reasoning effort: `none`, `low`, `medium`, `high` or `xhigh` |
| `maxtools` | `10` | The maximum number of tool calls in one request |
| `maxtokens` | `16384` | The maximum number of tokens in one answer |
| `temperature` | `0.3` | A lower value gives answers which change less |
| `memory` | `20` | The number of recent channel messages which the agent remembers. `0` turns it off |
| `memoryexpiry` | `30` | The minutes without activity before the agent forgets a channel |
| `useembed` | `true` | `false` sends answers as plain text |
| `showactivity` | `true` | Shows a live message with the steps of the agent |

In `data/ai-agent.yml`, some of these settings have longer names, for example `modelName`, `maxTokens`, `reasoningEffort` and `memoryIdleExpiryMinutes`. To turn off reasoning, set `reasoningEffort` to an empty value in the file.

These settings are only in `data/ai-agent.yml`:

- `models`: a list of models for providers like OpenRouter. When the first model fails, the provider tries the next one.
- `nameTriggerEnabled`: the agent starts when a member says the name of the bot. Default `true`.
- `followUpWindowSeconds`: the seconds after an answer in which a member can continue without a mention. Default `120`. `0` turns it off.
- `allowedTools`: the tools which the agent can use. Empty means all tools.
- `customHeaders`: extra HTTP headers for each request to the provider.

## Choose who can use the agent

The bot owner can always use the agent. Other people can use it only when you allow them. The bot ignores everyone else.

| Command | What it does |
|---|---|
| `.agentwhitelist @user` / `.aiwl` | Allows one user, or removes the user again |
| `.agentwhitelistrole <role>` / `.aiwlr` | Allows all members of a role |
| `.agentwhitelistserver [server id]` / `.aiwls` | Allows all members of a server (default: this server) |
| `.agentwhitelistlist` / `.aiwll` | Shows who is allowed |

If patronage is on (`isEnabled: true` in `data/patron.yml`), active patrons can also use the agent.

An alias, a scheduled command or a command which the agent runs never starts the agent.

## Edit the prompts

Two files in `data/ai/prompts/` control how the agent talks. The bot creates them on the first start.

- `SOUL.md`: who the bot is (name, personality, tone).
- `OPERATOR.md`: your rules for the agent (topics, limits, things it must not do).

Every server sees the same two files. For rules of one server, use [skills](usage.md#skills).

To edit a file in Discord, type `.aiprompt`, select **SOUL** or **OPERATOR**, then click **Edit**. You can also edit the files directly. The bot loads a changed file in about one second. A file can have up to 20 KB.

!!! warning
    The Discord editor holds only 4,000 characters. If a file is longer, edit it on disk. When you save a longer file in the Discord editor, the bot keeps only the first 4,000 characters.

You can use these placeholders in the prompts:

- `{botName}` and `{botId}`: the name and the ID of the bot.
- `{guildName}`, `{channelName}` and `{userName}`: these work too, but they make the prompt different for each server, channel and user. Then the provider cannot cache the prompt, and each request costs more. The agent already gets these values in a separate message.

## Troubleshooting

- **The agent does not answer.** Make sure that `enabled` is `true`, `aiApiKey` is set and the user is allowed. Look for errors from the provider in the bot's log.
- **The log shows `AI embedding runtime (ONNX) failed to load`.** The agent is off until the next start. On Windows, install the Microsoft Visual C++ Redistributable (x64).
- **The log shows `unknown parameter: tools`.** The model does not support tool calling. Pick a different model.
- **A change in `SOUL.md` does not work.** The log shows `PromptLibrary: loaded SOUL` after the bot loads the file. If you do not see it, save the file again or restart the bot.
