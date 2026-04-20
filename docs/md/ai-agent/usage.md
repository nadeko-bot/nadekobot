# Usage and Skills

## Talking to the agent

You have four ways to trigger the agent. Pick whichever is convenient.

### Ping the bot (easiest)

Mention the bot at the start of your message. Everything after the mention is the query.

```
@Nadeko what's my currency balance?
@Nadeko give 500 to @Alice
```

This always runs the agent. No intent check, no prefix needed. Requires permission to use the agent (see the bottom of this page).

### The `.agent` command

```
.agent show me who has the most xp this month
```

Same result as a ping, just using the bot's prefix instead of a mention. Handy if pings are off in the channel or if you like typing.

### Reply to a bot message

Reply to any message the bot sent. If your reply reads like a question or a command, the agent picks it up. If it reads like a reaction ("lol", "thanks"), the bot ignores you.

### Say the bot's name

If the owner enabled name triggers, mentioning the bot by name works too:

```
hey Nadeko, warn @Bob for spamming
what's my xp Nadeko?
```

Same intent check as replies. Casual mentions of the name won't trigger the agent. Owners can turn this off if it's noisy.

### Follow-up messages

Right after the agent replies, you get a short follow-up window (a few seconds, set by the owner). Inside that window, any message you send in the same channel counts as a continuation. No ping or prefix needed.

```
@Nadeko how much currency does Alice have?
  → Alice has 5,000 currency.
and Bob?
  → Bob has 1,200 currency.
```

### Cancelling a turn

`.agentcancel` stops the current session for your user. Use it if the agent is slow or stuck in a loop. Your conversation history for that session is dropped.

## Skills

Skills are short instructions server admins add to shape the agent inside their server. They're stored in the bot's database.

- A skill is a name plus an instruction, up to 2000 characters.
- Every guild can have up to 50 skills.
- Each enabled skill is prepended to the user's message before the agent sees it, wrapped in a `[SERVER INSTRUCTIONS]` block.

### Guild skills

Apply to every agent turn in the server, regardless of channel.

| Command | What it does |
|---|---|
| `.agentskilladd <name> <instruction>` | Create a skill. |
| `.agentskillremove <name>` | Delete it. |
| `.agentskilltoggle <name>` | Enable or disable without deleting. |
| `.agentskilllist` | Show all skills with their toggle state. |

Requires **Administrator** in the guild.

Example:

```
.agentskilladd support-tone Always reply in a calm, professional tone. Never joke about tickets.
.agentskilladd no-price-talk Do not quote prices. If asked, say to check #pricing.
```

### Channel skills

Same idea, but only active when the agent runs in that specific channel.

| Command | What it does |
|---|---|
| `.agentchannelskilladd <name> <instruction>` | Create a channel skill. |
| `.agentchannelskillremove <name>` | Delete it. |
| `.agentchannelskilltoggle <name>` | Enable or disable. |
| `.agentchannelskilllist` | Show channel skills for the current channel. |

Requires **Administrator**.

Good fit for channels with their own rules: a `#support` channel that wants formal tone, a `#memes` channel that wants the opposite, a `#announcements` channel where the agent should stay quiet.

### Guild vs channel skills at a glance

| | Guild skill | Channel skill |
|---|---|---|
| Scope | every channel in the guild | one channel |
| Row key | (guild, name) | (guild, channel, name) |
| Use when | rule is about the server as a whole | rule is about one space |

You can have the same name in both places -- a guild-level `tone` skill and a channel-level `tone` skill that overrides it in one channel.

## What the agent can do

Writes go through real bot commands. The agent calls `run_command`, which runs the command as the invoking user under the normal permission pipeline. If you don't have `ManageMessages`, neither does the agent when acting for you.

Reads go through **data tools** -- typed, JSON-returning functions registered per feature (xp, waifus, reminders, moderation, etc.). The agent doesn't have them all loaded at once; it calls `search_data_tools` with a query like "user xp stats" and the infrastructure returns the best matches to pull into context.

## Privacy

- Conversation history is per user and lives in memory only. It's dropped when `.agentcancel` fires or the bot restarts.
- User-private reads (your reminders, your quests) are keyed on the invoking user's ID. The agent can't read another user's private data even if you ask it to.
- A sanitizer runs on display names and channel names before they reach the model, so mention tokens and prompt-injection tricks in names are neutralized.

## Permission to use the agent

The bot owner sets this. Typically:

- Bot owner: always allowed.
- Active patron (if patronage is enabled): allowed. See [Donate](../donate.md).
- Everyone else: ignored silently. Pings, replies, name triggers, and `.agent` all do nothing.

If nothing happens when you try to use the agent, the bot is either not configured or your user isn't on the allow list. Ask the bot's host.
