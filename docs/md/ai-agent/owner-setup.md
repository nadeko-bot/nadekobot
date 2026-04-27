# Owner Setup

This page is for bot owners (self-hosters). If you just want to use the agent on a server where someone else is hosting the bot, see [Usage and Skills](usage.md).

## 1. Pick a model provider

You need an OpenAI-compatible API that supports **tool / function calling**. Known to work:

- OpenAI direct (gpt-5.x, o-series)
- OpenRouter (fallback routing across providers)
- Anthropic via OpenRouter or a compatibility shim
- Self-hosted vLLM / llama.cpp with an OpenAI-compatible front-end, if the model supports tool calls

A model without tool calling will just talk -- it won't be able to run commands or read data.

## 2. Set credentials

Open `data/ai/ai-agent.yml` (created on first startup if missing). Key fields:

```yaml
ModelName: gpt-5.4            # one model
Models:                       # or a list for providers that route (e.g. OpenRouter)
  - gpt-5.4
  - claude-3.7-sonnet
BaseUrl: https://openrouter.ai/api/v1
ApiKey: sk-or-v1-...
ReasoningEffort: medium       # for models that support it
UseEmbed: true                # false = plain-text replies
```

Restart the bot after editing creds or `BaseUrl`. `ModelName`, `Models`, `ReasoningEffort`, and `UseEmbed` can also be changed at runtime via the config commands (see `.h .config aiagent`).

## 3. Understand the prompt files

Path: `data/ai/prompts/`. Layout:

```
data/ai/prompts/
├── SOUL.md              owner-edited, bot identity
└── OPERATOR.md          owner-edited, rules for the agent
```

- `SOUL.md` and `OPERATOR.md` are seeded automatically on first run.
- Placeholders: `{botName}`, `{botId}`, `{guildName}`, `{channelName}`, `{userName}` are replaced at turn time.
- Size limit: 20 KB per file.
- A filesystem watcher picks up edits with a 1 second debounce. No restart needed.

!!! note "Scope"
    These files are process-global. Every guild your bot serves sees the same SOUL and OPERATOR. Per-guild behavior belongs in skills.

## 4. Owner commands

`.aiprompt` is owner-only. Run it with no arguments to get a panel with two buttons (SOUL and OPERATOR). Clicking either is the same as running `.aiprompt soul` or `.aiprompt operator` directly: the bot shows the current contents and offers an Edit button that opens a Discord modal with Save / Cancel built in.

Saves go through a `.tmp` file and atomic rename, so an interrupted edit won't leave a half-written prompt on disk.

## 5. Access control

The agent won't reply to arbitrary users by default. Current policy:

- The bot owner can always use it.
- Active patrons can use it if patronage is enabled (`patronage.enabled: true` in `data/patronage.yml`).
- Everyone else gets ignored silently.

Cache: allow/deny is cached per user for 1 minute to avoid a DB hit on every message.

## 6. Troubleshooting

- **Agent doesn't respond.** Check logs for auth errors on the provider, confirm the user is an owner or active patron, and confirm the model supports tool calling. A 400 "unknown parameter: tools" is the fingerprint of a model without tool-call support.
- **Edits to `SOUL.md` don't show up.** Look for `PromptLibrary: loaded SOUL ...` in the logs after your save. If you don't see it, the watcher didn't fire -- save again or restart the bot. Some editors write via replace-rename; the watcher handles both.
- **Agent says it can't find a tool.** Data tools are discovered on demand via `search_data_tools`. The agent is supposed to call that first when looking for reads. If it's refusing to search, your prompt may be steering it away -- check `SOUL.md` and `OPERATOR.md`.
- **Prompt is too large.** If SOUL + OPERATOR + command list exceed the model's context budget, trim them.
