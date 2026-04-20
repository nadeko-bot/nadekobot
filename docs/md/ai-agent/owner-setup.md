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
EnabledModules:               # which modules/*.md to include in the system prompt
  - data-tools
  - concise
```

Restart the bot after editing creds or `BaseUrl`. `ModelName`, `Models`, `ReasoningEffort`, `UseEmbed`, and `EnabledModules` can also be changed at runtime via the config commands (see `.h .config aiagent`).

## 3. Understand the prompt files

Path: `data/ai/prompts/`. Layout:

```
data/ai/prompts/
├── SOUL.md              owner-edited, bot identity
├── OPERATOR.md          owner-edited, rules for the agent
├── modules/             owner-edited, optional behavior modules
│   ├── concise.md
│   └── no-profanity.md
└── examples/            read-only reference personas
    ├── SOUL-catgirl.md
    ├── SOUL-concise.md
    └── SOUL-helpful.md
```

- `SOUL.md` and `OPERATOR.md` are seeded automatically on first run. Pick one of `examples/SOUL-*.md` as a starting point if you want.
- Files in `modules/` are discovered by filename. Empty files show up in `.apromptmodule` so you can toggle them without touching disk.
- Placeholders: `{botName}`, `{botId}`, `{guildName}`, `{channelName}`, `{userName}` are replaced at turn time.
- Size limits: 20 KB for `SOUL.md` / `OPERATOR.md`, 8 KB per module.
- A filesystem watcher picks up edits with a 1 second debounce. No restart needed.

!!! note "Scope"
    These files are process-global. Every guild your bot serves sees the same SOUL and the same enabled modules. Per-guild behavior belongs in skills, not modules.

## 4. Owner commands

All of these are bot-owner-only.

| Command | What it does |
|---|---|
| `.aprompts` | List SOUL, OPERATOR, and every module with its size and enabled state. |
| `.apromptshow <path>` | Print the contents of a prompt file. |
| `.apromptedit <path> <content>` | Write a prompt file. Respects size limits. Path is relative to the prompts dir. |
| `.apromptmodule <name>` | Toggle a module on or off. No file changes; stored in config. |
| `.apromptreload` | Force a rebuild of the in-memory snapshot across every shard. |
| `.apromptpath` | Print the absolute path of the prompts directory. |

`.apromptedit` writes through a `.tmp` file and atomic rename, so an interrupted edit won't leave a half-written prompt on disk.

## 5. Access control

The agent won't reply to arbitrary users by default. Current policy:

- The bot owner can always use it.
- Active patrons can use it if patronage is enabled (`patronage.enabled: true` in `data/patronage.yml`).
- Everyone else gets ignored silently.

Cache: allow/deny is cached per user for 1 minute to avoid a DB hit on every message.

## 6. Troubleshooting

- **Agent doesn't respond.** Check logs for auth errors on the provider, confirm the user is an owner or active patron, and confirm the model supports tool calling. A 400 "unknown parameter: tools" is the fingerprint of a model without tool-call support.
- **Edits to `SOUL.md` don't show up.** Look for `PromptLibrary: loaded SOUL ...` in the logs after your save. If you don't see it, the watcher didn't fire -- try `.apromptreload`. Some editors write via replace-rename; the watcher handles both.
- **Agent says it can't find a tool.** Data tools are discovered on demand via `search_data_tools`. The agent is supposed to call that first when looking for reads. If it's refusing to search, your prompt may be steering it away -- check `SOUL.md` and enabled modules.
- **Prompt is too large.** Check `.aprompts`. If SOUL + OPERATOR + modules + command list exceed the model's context budget, disable a module or trim SOUL.
