# Changelog

*a,c,f,r,o,d*


## [7.2.6] - 23.05.2026

### Fixed

- `.random*` commands now use the proper cdn url
- avatar url fixed in searches commands too
- small docs fix

## [7.2.5] - 14.05.2026

### Fixed

- The Linux CLI guide's systemd setup snippet now works in non-bash shells (fish, nushell)
- `.feed` and `.ytuploadnotif` messages now actually trigger `@everyone`, `@here`, and role pings when the user who set up the feed has the required permissions

### Removed

- Removed cache_control ai header

### Dev

- Some compilation warnings fixed

## [7.2.4] - 03.05.2026

### Added

- AI agent can now send fully formatted embeds via `send_message`

### Changed

- `.wlb` should now show 3x3 embed with inline fields
- `.servernotify` help now documents that standard `%user.*%` and `%server.*%` placeholders work in any notify message.
- Password-protected plant prompts now tell users to type `.pick <password>`

### Fixed

- `.wlb` no longer shows the opt-in guide when the leaderboard is empty
- `.ea` (emojiadd) now has correct parameters in help
- Image upload commands (`.emojiadd`, avatar, banner, server icon, server banner, club icon) now accept WebP images

### Dev
- Embed JSON Simplification internally

## [7.2.3] - 28.04.2026

### Fixed

- Fixed `.repeat` if interval is greater than 24h

## [7.2.2] - 27.04.2026

### Added

- `.patronadd` (`.pa`) owner command: manually grant a user 30 days of patron benefits with the specified cent amount
- `.aiprompt` owner only command to manage SOUL.md and OPERATOR.md files
- AI agent system prompt is now composed from Markdown files under `data/ai/prompts/` instead of a YAML property.
    - `SOUL.md` for bot identity
    - `OPERATOR.md` for operator rules
    - and drop `.md` files into `modules/` for optional behavior modules (personas, specialities, etc.).
- Per-channel AI agent skills via `.acsa`, `.acsr`, `.acst`, `.acsl` - channel skills only apply when the agent runs in that specific channel
- `.aprompts`, `.apromptshow`, `.apromptedit`, `.apromptreload`, `.apromptmodule`, `.apromptpath` owner commands for managing prompt files
- Prompt files hot-reload via file system watcher - edit a file, changes apply automatically
- AI agent data layer
    - This version covers members, channels, XP, currency, waifus, warnings, reminders, music, server config, moderation, permissions, quests, and self-assignable roles
    - more will be added in the future
- Waifu and AI system guide added to the docs under Features Explained

### Changed

- `.patrons` is now paginated with 10 patrons per page; tier headers repeat on continuation pages
- `.wlb` leaderboard should look better
- Faster startup: DI container build, module registration, and type reader loading now run in parallel with Discord login instead of sequentially after it
- DM notifications from the new `.notify` system have a disable hint

### Fixed

- Channel blacklist (`.blc`) now actually blocks messages from blacklisted channels
- Anti-raid no longer silently degrades after its first trigger
- Link fixer service only loads the data it needs
- Some crash fixes

### Removed

- Prompt modules (`data/ai/prompts/modules/`) and example personas (`data/ai/prompts/examples/`). Only `SOUL.md` and `OPERATOR.md` remain.

### Dev

- AiAgent reworked
- Database cleanup/migrations
- Massive optimizations for message hot path, bot startup, and queries
- Misc service cleanup

## [7.1.26] - 17.04.2026

### Added

- `.waifulb` alias for `.wlb` (waifu leaderboard)
- `useembed` config option for AI agent to control whether responses are sent as embeds or plain text

### Changed

- AI agent messages that exceed Discord character limits are now split across multiple messages at word boundaries, instead of being truncated
- Currency drops (plant/pick) now display the pick instructions on a new blockquoted line for better readability

### Fixed

- `.config aiagent enabled true` no longer reverts to `false` when AI credentials are not set; a warning is logged instead
- "Waifu lb seen" quest objective now correctly tracks the waifu leaderboard command


## [7.1.25] - 13.04.2026

### Added

- User-level DM notification system with `.notify` command - users can toggle individual notification types on/off via interactive buttons
- Club application accepted DM notification when your application is approved
- `.queuefind` (`.qfind`, `.qf`) command to search the music queue by track title, shows up to 10 matches with queue positions

### Changed

- Server-level notify commands renamed from `.notify` to `.servernotify` (`.snotify`, `.sn`)
- Music module available to bot owners and patrons on the public bot

### Fixed

- Various URL validation and input sanitization fixes
- Animal race refund, giveaway winner selection, transaction cleanup
- yt-dlp process handling and connection pooling
- Command handler config now applies without restart
- `ConsoleOutputType.None` now actually suppresses logs
- Music queue track links now display correctly as clickable links in `.lq`, `.np`, and other music commands


### Dev

- Lock-free copy-on-write for custom behaviors on message hot path
- Nullable annotations on command handler and behavior interfaces
- Use ArgumentList for yt-dlp/ffmpeg/ffprobe process calls


## [7.1.24] - 11.04.2026

### Fixed

- Web search, club icons, radio resolver protection
- Encode user input in Osu, weather, movie, dictionary API queries
- Sanitize shell-relevant characters in yt-dlp arguments
- Enforce file extension whitelist in local track resolver
- pg to sqlite migration
- Permission checker failure
- Guild perm checks
- TicTacToe race condition

### Changed

- Add 15s timeout for RSS feed fetching
- `.wlist` now shows buy price and total backed separately
- Voice XP batch allocation cleanup

### Dev

- Use IHttpClientFactory for radio resolver

## [7.1.23] - 10.04.2026

### Changed

- Local music track enqueue no longer spawns ffprobe per file; duration is resolved lazily when the track plays and cached in memory

### Removed

- PostgreSQL and MySQL database support. Existing PostgreSQL data is automatically migrated to SQLite on first startup after update

## [7.1.22] - 07.04.2026

### Fixed

- `.config` command could not get or set any camelCase properties (e.g. `webEngine`, `imgEngine`, `ytProvider`)

### Added

- Brave Search provider for `.search` and `.image` commands (set `webSearchEngine: Brave` or `imgSearchEngine: Brave` in `searches.yml`, requires `braveSearchApiKey` in `creds.yml`)
- DuckDuckGo scrape search provider for `.search` and `.image` commands (set `webSearchEngine: DuckDuckGo` or `imgSearchEngine: DuckDuckGo` in `searches.yml`, no API key needed)
- Bunch of ai agent stuff
    - Custom HTTP headers support in AI agent config (e.g. `X-OpenRouter-Title`)
    - `models` fallback list of models - `model` takes precedence
- Per-server AI agent skills: named, toggleable instruction slots that guide agent behavior
  - `.agentskilladd`, `.agentskillremove`, `.agentskilltoggle`, `.agentskilllist` (admin-only)
- `.patrons` owner-only command to list all active patrons grouped by tier
- `.honeypot` now supports punishment modes: `.honeypot ban` for permanent ban, `.honeypot softban` (default) for ban + unban

### Fixed

- AI agent channel history no longer breaks when messages contain Discord emojis, mentions, or other angle-bracket markup
- Music queue and now-playing embeds no longer break when video titles contain markdown characters (`*`, `_`, `~`, etc.)

## [7.1.22] - 05.04.2026

### Added

- `.credsreload` owner command to hot-reload credentials from `data/creds.yml`
- AI agent can now ask clarifying questions before acting (`ask_user` tool)
- `.log AvatarUpdated`, `.log UsernameUpdated`, `.log NicknameUpdated` for fine-grained control over user update logging
  - Existing `UserUpdated` subscribers are auto-migrated to all three new types
  - `UserUpdated` renamed to `RolesUpdated` (now only logs role changes)

### Changed

- Improved userrole command descriptions

### Dev

- Removed unused file watcher on `creds.yml`

## [7.1.21] - 03.04.2026

### Added

- `.log Honeypot` to toggle honeypot logging to a specific channel
  - Honeypot bans/unbans no longer trigger UserBanned/UserUnbanned events for `.log`

### Dev

- Replaced `LogSetting` wide table with `LogChannel` and `LogIgnore` tables
- Added `[PatronOnly]` attribute to require active patron status, shown with a heart icon in `.cmds` and as a requirement in `.h`
  - No commands are patron only right now

### Removed

- Removed the manager purchase buy window restriction (managers can now be purchased at any time during a cycle since payouts use cycle-start snapshots)

## [7.1.20] - 03.04.2026

### Added

- `.anilist` command

### Changed

- `.wlb` leaderboard now has a better layout

## [7.1.19] - 01.04.2026

### Added

- `.logignore` now accepts category channels - all channels within an ignored category are excluded from logging

### Changed

- `.reroe` parameter order changed to match `.reroa` convention: messageId, emote, role (was messageId, role, emote)
- Expressions with a `-` response and reactions set will now react to the trigger message instead of being fully suppressed

### Fixed

- Fixed `.reroe` not working with sqlite

## [7.1.18] - 01.04.2026

### Added

- New `data/utility.yml` config file with configurable per-server limits for repeaters, scheduled commands, and live channels
  - Use `.config utility` to view/edit
- Added `.xpt` command - text-based embed alternative to `.xp` image card
- Added `.reroe` command to change the emote for an existing reaction role entry


### Changed

- `.rerorm` now accepts an optional role to remove a specific role's entries from a reaction role message (with paginated selection when multiple emotes map to the same role)

### Fixed

- Fixed XP formula defaulting to (0, 0) instead of (9, 27)

### Dev

- Reworked config system: `.Data` now returns a volatile reference (zero allocations) instead of cloning on every read. Writes use `Lock` + atomic swap. Removed `[Cloneable]` and `ICloneable<T>` from all config types. Fixed double-publish bug in `SetSetting`.
- Removed expression tree traversal and `PropertyInfo` reflection from `AddParsedProp`. All config property access now uses direct static lambdas.

## [7.1.17] - 30.03.2026

### Added

- `.warnmsg` / `.warnmsgreset` / `.warnmsgtest` commands for customizable warn DMs, similar to `.banmsg`

### Changed

- Console log messages now say "intercepted" instead of "blocked" for early command interception
- `.exo` now requires confirmation before toggling expression override mode, as it is a potentially highly destructive action
- All confirmation prompts now use interactive buttons instead of typing yes/no
- Owner DM forwarding is now always on. Each owner individually opts out via `.fw`. Removed `.fwtoall`.
- `.fwtoch` now notifies all non-opted-out owners when channel forwarding is enabled

## [7.1.16] - 30.03.2026


### Changed

- global only: Ai agent is now owner + patron (still alpha)

### Fixed

- Fixed initial database creation broken in the last patch

### Dev

- Upped to .net9
- Replaced all `object` lock fields with `System.Threading.Lock` for ~25% faster locking
- Eliminated allocating string normalization (ToLower/ToUpper) in type readers, config service, and command lookup by using case-insensitive comparers

## [7.1.15] - 29.03.2026

### Added

- `.exo` / `.exoverride` - toggle expression override mode per server. When enabled, adding an expression with a trigger that already exists will replace the old one(s) instead of creating a duplicate.
- `.exs` added - searches for expressions based on trigger or content

### Changed

- `.exe` will now accept either a trigger or id
    - if there are multiple expressions with the same trigger you will get a paginated prompt to pick which one to edit
- Old `.exs` (show) was renamed to `.exp` (print).
    - `.exs` is now search.

### Fixed

- `.inrole` now works with multi-word role names without having to add double quotes

## [7.1.13] - 27.03.2026

### Changed

- `.exd` will now accept either a trigger or id
    - if there are multiple expressions with the same trigger you will get a paginated prompt to pick which one to delete

### Fixed

- .onnx embedding for ai agent will be properly cached and won't re-do it every startup unless commands changed
- Invidious playlist loading now uses the correct API endpoint
- Fixed Invidious search queries with special characters

## [7.1.12] - 25.03.2026

### Added

- Fish items can now have level req.
- `.xpformula` / `.xpf` - configure the per-guild XP leveling curve (a = slope, c = base cost)

### Changed

- Music will start playing faster
- Music now uses high resolution timers on all 3 platforms (meaning less lag and less cpu usage)
- Fishing spot coins are now used with `.fiuse` like other items and give you a random biome

### Fixed

- Ai agent will now auto-disable if no api key is provided

### Removed

- `.fishpch` removed

## [7.1.11] - 23.03.2026

### Changed

- `.google` / `.search` will no longer have `.s` as the alias as it conflicts with `.stop`

### Fixed

- Allow cache for big files too
- Fallback to direct streaming when caching fails

## [7.1.10] - 23.03.2026

### Added

- Audio file caching system saves opus data to disk, config max cache size in `data/music.yml`
- Opus passthrough when available

### Changed

- Music quality presets simplified High / Medium only.

### Fixed

- Voice gateway reconnect loop spamming logs
- Hangman message never reposting

## [7.1.9] - 21.03.2026

### Fixed

- Waifu query throwing errors on some locales

## [7.1.8] - 21.03.2026

### Fixed

- Dragging the bot to a different music channel while music is playing will now correctly keep the music going

## [7.1.7] - 21.03.2026

### Fixed

- More DAVE fixes/changes

## [7.1.5] - 21.03.2026

### Fixed

- More music/voice edge-case fixes
- Fixed RSS feeds being silently deleted when the bot was kicked from a guild that shared a feed URL with other guilds
- Fixed RSS feed error counter being incremented on Discord send failures instead of only on fetch failures
- RSS feeds now send a notification to the channel when a feed is auto-removed due to repeated failures

### Changed

- Increased RSS feed error threshold before auto-removal from 100 to 500
- RSS feed fetcher now sends a modern browser User-Agent header to avoid blocks from Reddit and Cloudflare-protected sites

## [7.1.4] - 21.03.2026

### Fixed

- Music fixes, logging
- Fixed AI command search crashing on startup on some systems due to ONNX runtime thread affinity errors

## [7.1.3] - 21.03.2026

### Fixed

- Fixed docker build

## [7.1.2] - 21.03.2026

### Added

- `.ncnuke` owner-only command to reset all pixels owned by a specified user
- `.shardstats` now also show uptime over last 1 hour

### Changed

- `.ncpixel` now shows the pixel owner's name and ID

### Fixed

- Fixed music going silent when someone joins/leaves the voice channel.

### Removed

- GrpcApi removed. Bloat.

### Dev

- Fixed build warnings

## [7.1.1] - 20.03.2026

### Added

- New AI agent system - beta, owner only for now (replaces chatterbot/cleverbot/nadeko ai)
- `.notify` now supports `fishcurrency` event to track when users fish out currency

### Changed

- Reorganized Administration module to fix `.cmds admin` exceeding discord's 25-option limit
- `gptApiKey` in `creds.yml` renamed to `aiApiKey` (auto-migrated on startup)
- Patrons now get 3 waifu actions per cycle (hug/kiss/pat/nom)

### Fixed

- Fixed `.masskick` actually banning users instead of kicking them
- Fixed some waifu console errors
- Countless command strings fixed

### Removed

- Chatterbot/cleverbot and old nadekoAi have been removed in favor of the new ai agent system


## [7.0.12] - 17.03.2026

### Added

- `.nom` command that feeds a waifu to improve food

### Changed

- `.hug`, `.kiss`, `.pat`, `.nom` now show a random gif in the response
- `.hug`, `.kiss`, `.pat`, `.nom` now support multiple users at once (e.g. `.hug @user1 @user2`) and arbitrary strings - non waifus don't receive the effect
- `.wbuy` now asks for confirmation

## [7.0.11] - 17.03.2026

### Changed

- Hangman will now re-post once every 5 messages
- `.log` now supports UserMuted event logging (mute/unmute)
- You can now `.fish` out currency
- `.hug`, `.pat`, `.kiss` now work on non-waifu users with a "no effect!" message instead of an error

### Fixed

- yt-dlp errors due to unavailable formats

## [7.0.10] - 16.03.2026

### Changed

- Better randomness
- `.qse` reworked to search across all fields
- `.join` now accepts a voice channel, optionally

## [7.0.9] - 15.03.2026

### Fixed

- Docker .so library overwrite fix
- Version notification formatting

## [7.0.8] - 15.03.2026

### Fixed

- Important new waifu system related fixes

## [7.0.7] - 15.03.2026

### Added

- yt-dlp now automatically uses cookies from `data/ytcookies.txt` if the file exists, enabling downloads from sites that require authentication

### Fixed

- Antispam now detects repeated attachment spam (images, files) in addition to text-only spam
- Fixed YouTube RSS feed thumbnails not showing in embeds
- Waifu system code reworked, again. More scalable

### Changed

- `.inrole` now accepts multiple roles and shows users who have all of them

### Dev

- Waifu system code cleanup and improvements

## [7.0.6] - 14.03.2026

### Added

- Translations for 16 languages
- `.wlb` now sorts by backing by default; use `.wlb price` for price-based ranking

### Fixed

- Fixed version update notification being re-sent on every bot restart
- Fixed missed waifu cycle payouts when the bot restarts across a cycle boundary

### Dev

- Added tests for locale string validation

## [7.0.5] - 13.03.2026

### Changed

- Waifu card layout and localization improvements
- `.wlb` now sorts by backing by default; use `.wlb price` for price-based ranking

### Fixed

- Fixed Docker builds
- Fixed publish strings merger

## [7.0.0] - 13.03.2026

### Added

- DAVE protocol support for Discord voice end-to-end encryption (E2EE)
- Added `.hands` command - see a list of currently raised hands in your stage channel, in the chronological order
- Added `.lineup` commands - users can line-up with an optional reason
  - Moderators commands:
    - `.lineupcreate` - Creates a new lineup that users can join
    - `.lineupnext` - Removes the next user from the lineup and sends a message with that user's name
    - `.lineupclear` - Clears the lineup
    - `.lineupremove` - Removes a user from the lineup
  - User Commands:
    - `.lineup` - join the line
    - `.lineuplist` - see the current lineup
    - `.lineupleave` - leave the lineup if you've previously joined
- Added `.fishspotchange` command - use a Spot Coin to change the fishing spot of the current channel
- Added `.mereload` / `.medusareload` command - unloads and reloads a medusa in one step

### Changed

- Waifus are now a separate top-level module (moved out of Gambling)
- Owner commands are now a separate top-level module in `.mdls`
- Waifus reworked
  - New backing system: fans delegate bank balance to waifus, earning returns each cycle
  - Managers replace claimers - buy the manager position to earn a cut of returns
  - Managers can manage any number of waifus; fan backing limited to one waifu
  - New `.waifuhelp` / `.whelp` command - shows an overview of the waifu system, roles, commands, and cycle mechanics
  - New `.waifuoptin` / `.woptin` command - opt into the waifu system (costs currency)
  - New `.waifuoptout` / `.woptout` command - opt out of the waifu system
  - New `.waifuback` / `.wback` / `.wfan` command - become a fan of a waifu (toggle backing on/off, or switch)
  - New `.waifubuy` / `.wbuy` / `.wclaim` command - buy the manager position for a waifu
  - New `.waifufee` / `.wfee` command - set your waifu fee percentage (1-5%)
  - New `.waifufans` / `.wfans` command - show all fans backing a waifu and their last cycle earnings
  - New `.waifugiftshop` / `.wgs` command - shows today's rotating gift shop items
  - New `.waifuleaderboard` / `.wlb` command - leaderboard ranked by realized return rate
  - New `.waifuresign` command to resign from managing a specific waifu
  - Use `.wpayout` or the Collect button on `.w` to claim currency
  - New `.waifulist` / `.wlist` command to view all waifus you manage
  - Mood and food stats with decay, improved by gifts and actions (hug/kiss/pat)
  - Daily rotating gift shop (6 items across 6 price tiers)
  - Gift tracking: all received gifts are persisted and displayed on the waifu card
  - Cycle-based payout system with projected earnings on the card
  - Waifus with price >= 1000 are automatically migrated from the old system; claimers become managers, gift history is preserved
  - New `data/waifu.yml` config file for selfhosters to configure the waifu economy (cycle length, rates, fees, caps, etc.)
- Nunchi renamed to CountUp, with visual improvements
- Fishing leaderboard and stars leaderboard now show 10 entries per page instead of 9
- Music player: improved packet timing to prevent audio drift, sends silence frames between tracks to avoid Opus artifacts

### Fixed

- Fixed mute role not being saved correctly in some cases
- Fixed .vcrole resetting on bot restarts

### Removed

- Removed `.medusasearch` / `.mesearch` command
- Removed old waifu commands: `.waifureset`, `.waifuclaims`, `.waifuclaim`, `.waifutransfer`, `.divorce`, `.affinity` (replaced by the new waifu system)

### Dev

- DAVE (Discord Audio/Video Encryption) native library (`libdave.so` / `libdave.dll`) shipped with the bot
- Voice client: pre-allocated RTP/nonce buffers, reduced per-packet heap allocations
- Strings now live next to each feature, they are merged in a pre-build msbuild task and sent to output
- Responses, cmds and names (previously aliases) are now all .yml
- Administration and Searches commands split into smaller submodules

## [6.1.24] - 08.02.2026

### Fixed
- Fixed yt-dlp in Docker

## [6.1.23] - 07.02.2026

### Changed
- Better error message for missing guild permissions in role commands

### Fixed
- Fixed Steam API not loading all games
- Docker should have proper music now and work on arm and x64

## [6.1.22] - 01.02.2026

### Added
- If all image emoji slots are taken, the bot will suggest creating a 2 frame gif instead

### Fixed
- Potential fix for the Steam API

## [6.1.21] - 05.12.2025

### Fixed
- Fixed the voice encryption error

## [6.1.20] - 11.11.2025

### Added
- Added `.fishstarslb` / `.fislb` command to show top anglers by stars collected
- Added `.xpowned` command to view owned xp items
- Added `.conf search feeds.maxcount` configuration option for search feeds

### Changed
- `.edit` now adds ✅ reaction on successful execution
- `.playlists` output is now paginated for better readability

### Fixed
- Role hierarchy checks in vcrole command (!!!)
- `qse` command now shows an output on no results
- Fixed occasional guild timezone null reference errors
- Fixed Page count display in `.pls title`
- Fixed Page 0 navigation in `.pls` commands
- Fixed reminder system crashing under specific conditions
- Fixed reminders longer than 30 days crashing the remind system
- Null reference edge cases in scheduled commands

## [6.1.19] - 09.07.2025

## Changed

- .warn should also work on users who are not in the server now
- dm after voting will tell you the platform you voted on

### Fixed

- Fixed unmute/ban/kick not getting removed from db
- Fixed default xp bar length
- Linkfix now considers subdomains a different site

### Changed

- Nunchi renamed to CountUp - visual improvements

### Fixed

- Fixed `.shopadd cmd`
- Scheduled commands will now be cleaned up if they're too long

### Removed

## [6.1.7] - 14.04.2025

### Fixed

- `.streamrole` fixed
- fixed `.ura` hierarchy check (it will let owners assign roles too)

## [6.1.6] - 12.04.2025

### Fixed

- QuestCommands no longer appear as a separate module

## [6.1.5] - 06.04.2025

### Fixed

- `.xpadd` will finally apply rewards and trigger notifications
- Fixed `.hangman` dislocation

## [6.1.4] - 04.04.2025

### Fixed

- Fixed .timely awarding multiple times
- Fixed .plant password - moved it down and right to avoid cutoff on phones

## [6.1.3] - 02.04.2025

### Fixed

- Bot will no longer fail to startup if ownerids are wrong

## [6.1.2] - 02.04.2025

### Fixed
- Fixed `.feed` not adding new feeds to the database

## [6.1.1] - 02.04.2025

### Added
- Added some config options for .conf fish

### Fixed
- Fixed a typo in fish shop
- .fishlb will now compare unique fish caught, instead of total catches
- hangman category now appears in .hangman output

## [6.1.0] - 28.03.2025

### Added
- Added Quest System!
  - Each user gets a couple of daily quests to complete
  - There are 10-15 different quests, each day you'll get 3
  - Upon completion of all dailies, the user will get a boost to timely and vote
  - `.quests` to see your quests
- Added Fishing Items!
  - `.fishop` to see a list of all available items for sale
  - `.fibuy` to buy an item
  - `.finv` to see your inventory
  - `.fiuse` to use an item. You can equip one of each item, except potions
    - You can equip one of each item
    - You can equip any number of potions, but they have limited duration and cant be unequiped
  - `.fili` will show your equipped item names, nad `.fish` will show bonuses
- Added `.fishlb` to see the top anglers
- Added `.notify <channel> nicecatch <message>` event
  - It will show all rare fish/trash and all max star fish caught on any server
  - You can use `.notifyphs nicecatch`  to see the list of placeholders you can use while setting a message
  - Example: `.notify #fishfeed nicecatch %user% just caught a %event.fish.stars% %event.fish.name% %event.fish.emoji%`
- Added prices to `.nczoom`
- Voting re-added, `.votefeed` to see all votes. Non-trivial setup required, check commits
- owner only `.massping` command for special situations

### Changed
- .notify commands now require Manage Messages permission
- .notify will now let you know if you can't set a notify message due to a missing channel
- `.say` will no longer reply
- `.vote` and `.timely` will now show active bonuses
- `.lcha` (live channel) limit increased to 5
- `.nc` will now show instructions

### Fixed
- Fixed `.antispamignore` restart persistence
- Fixed `.notify` events. Only levelup used to work
- Fixed `.hangman` misalignment
- Fixed bank quest

## [6.0.13] - 23.03.2025

### Added
- Added `.linkfix <old> <new>` command
  - If bot sees a message with the old link, it will reply to the message with a fixed (new) link
  - ex: `.linkfix twitter.com vxtwitter.com`
- Added `.roleicon role <icon_url / server_emoji>` command to set the icon of a role
- Added a captcha option for `.fish`

### Fixed
- Fixed youtube stream notifications in case invalid channel was provided
- `.lcha` (live channel) will now let you override an existing channel template even if you're at the limit
- Fixed `.shop` commands

### Removed
- removed `.xpglb` as it is no longer used

## [6.0.12] - 19.03.2025

### Fixed
- `.antispamignore` fixed for the last time hopefully
  - protection commands are some of the oldest commands, and they might get overhauled in future updates
  - please report if you find any other weird issue with them

## [6.0.11] - 19.03.2025

### Changed
- wordfilter, invitefilter and linkfilter will now properly detect forwarded messages, as forwards were used to circumvent filtering.

### Fixed
- `.dmc` fixed
- Fixed .streamremove - now showing proper youtube name when removing instead of channel id

## [6.0.10] - 19.03.2025

### Changed

- Live channels `.lcha` is limited to 1 for now. It will be reverted back to 5 in a couple of days at most as some things need to be implemented.

### Fixed

- `.antispam` won't break if you have thread channels in the server anymore
- `.ve` now works properly
- selfhosters: `.yml` parsing errors will now tell you which .yml file is causing the issue and why.

## [6.0.9] - 19.03.2025

### Changed

- `.cinfo` now also has a member list

### Fixed

- `.antispamignore` will now properly persist through restarts
- livechannels and scheduled commands will now be inside utility module as they should

## [6.0.8] - 18.03.2025

### Added

- Live channel commands
  - `.lcha` adds a channel with a template message (supports placeholders, and works on category channels too!)
    - Every 10 minutes, channel name will be updated
    - example: `.lcha #my-channel --> Members: %server.members% <--` will display the number of members in the server as a channel name, updating once every 10 minutes
  - `.lchl` lists all live channels (Up to 5)
  - `.lchd <channel or channelId>` removed a live channel

### Fixed

- `.antispamignore` fixed

## [6.0.7] - 18.03.2025

### Added

- Schedule commands!
  - `.scha <time> <text>` adds the command to be excuted after the specified amount of time
  - `.schd <id>` deletes the command with the specified id
  - `.schl` lists your scheduled commands
- `.masskick` added as massban and masskill already exist
- `.xpex` and `.xpexl` are back, as there was no way to exclude specific users or roles with .xprate

### Fix

- `.xprate` will now (as exclusion did) respect parent channel xp rates in threads
  - the xprate system will first check if a thread channel has a rate set
  - if it doesn't it will try to use the parent channel's rate

## [6.0.6] - 15.03.2025

### Added

- Added youtube live stream notification support for `.streamadd`
  - it only works by using an invidious instance (with a working api) from data/searches.yml

### Fixed

- Fixed `.hangman` not receiving input sometimes
- Fixed `.sfl` and similar toggles not working
- Fixed `.antialt` and other protection commands not properly turning on
- Fixed `%bot.time%` and  `%bot.date%` placeholders showing wrong date.
  - No longer a timestamp

## [6.0.5] - 14.03.2025

### Added

- Aded a title in `.whosplaying`
- Added a crown emoji next to commands if -v 1 or -v2 option is specified

### Changed

- `.remind` looks better
- `.savechat` no longer owner only, up to 1000 messages - unlimited if ran by the bot owner

### Fixed

- `.ropl` fixed

## [6.0.4] - 13.03.2025

### Added

- `.xp` system reworked
  - Global XP has been removed in favor of server XP
  - You can now set `.xprate` for each channel in your server!
    - You can set voice, image, and text rates
    - Use `.xpratereset` to reset it back to default
    - This feature makes `.xpexclude` obsolete
  - Requirement to create a club removed
  - `.xp` card should generate faster
  - Fixed countless possible issues with xp where some users didn't gain xp, or froze, etc
- user-role commands added!
  - `.ura <user> <role>` - assign a role to a user
  - `.url <user?>` - list assigned roles for all users or a specific user
  - `.urm` - show 'my' (your) assigned roles
  - `.urn <role> <new_name>` - set a name for your role
  - `.urc <role> <hex_color>` - set a color for your role
  - `.uri <role> <url/server_emoji>` - set an icon for your role (accepts either a server emoji or a link to an image)
- `.notify` improved
  - Lets you specify source channel (for some events) as the message output
- `.pload <id> --shuffle` lets you load a saved playlist in random order
- `.lyrics <song_name>` added - find lyrics for a song (it's not always accurate)

- For Selfhosters
  - you have to update to latest v5 before updating to v6, otherwise migrations will fail
  - migration system was reworked
  - Xp card is now 500x245
  - xp_template.json backed up to old_xp_template.json
  - check pinned message in #dev channel to see full selfhoster announcement
  - Get bot version via --version

### Changed

- `.lopl` will queue subdirectories too now
- Some music playlist commands have been renamed to fit with other commands
- Removed gold/silver frames from xp card
- `.inrole` is now showing users in alphabetical order
- `.curtrs` are now paginated
- pagination now lasts for 10+ minutes
- selfhosters: Restart command default now assumes binary installation

### Removed

- Removed several fonts
- Xp Exclusion commands (superseded by `.xprate`)

## [5.3.9] - 30.01.2025

### Added

- Added `.todo archive done <name>`
    - Creates an archive of only currently completed todos
    - An alternative to ".todo archive add <name>" which moves all todos to an archive

### Changed

- Increased todo and archive limits slightly
- Global nadeko captcha patron ad will show 12.5% of the time now, down from 20%, and be smaller
- `.remind` now has a 1 year max timeout, up from 2 months

### Fixed

- Captcha is now slightly bigger, with larger margin, to mitigate phone edge issues
- Fixed `.stock` command, unless there is some ip blocking going on

## [5.3.8] - 27.01.2025

### Fixed

- `.temprole` now correctly adds a role
  - `.h temprole` also shows the correct overload now

## [5.3.7] - 21.01.2025

### Changed

- You can now run `.prune` in DMs
  - It deletes only bot messages
  - You can't specify a number of messages to delete (100 default)
- Updated command list

## [5.3.6] - 20.01.2025

### Added

- Added player skill stat when fishing
  - Starts at 0, goes up to 100
  - Every time you fish you have a chance to get an extra skill point
  - Higher skill gives you more chance to catch fish (and therefore less chance to catch trash)

### Changed

- Patrons no longer have `.timely` and `.fish` captcha on the public bot

### Fixed

- Fixed fishing spots again (Your channels will once again change a spot, last time hopefully)
  - There was a mistake in spot calculation for each channel

## [5.3.5] - 17.01.2025

### Fixed

- .sar rm will now accept role ids in case the role was deleted
- `.deletewaifus` should work again

## [5.3.4] - 14.01.2025

### Added

- Added `.fish` commands
  - `.fish` - Attempt to catch a fish - different fish live in different places, at different times and during different times of the day
  - `.fishlist` - Look at your fish catalogue - shows how many of each fish you caught and what was the highest quality - for each caught fish, it also shows its required spot, time of day and weather
  - `.fishspot` - Shows information about the current fish spot, time of day and weather

### Fixed

- `.timely` fixed captcha sometimes generating only 2 characters

## [5.3.3] - 15.12.2024

### Fixed

- `.notify` commands are no longer owner only, they now require Admin permissions
- `.notify` messages can now mention anyone

## [5.3.2] - 14.12.2024

### Fixed

- `.banner` should be working properly now with both server and global user banners

## [5.3.1] - 13.12.2024

### Changed

- `.translate` will now use 2 embeds, to allow for longer messages
- Added role icon to `.inrole`, if it exists
- `.honeypot` will now add a 'Honeypot' as a ban reason.

### Fixed

- `.winlb` looks better, has a title, shows 9 entries now
- `.sar ex` help updated
- `.banner` partially fixed, it still can't show global banners, but it will show guild ones correctly, in a good enough size
- `.sclr` will now show correct color hexes without alpha
- `.dmcmd` will now correctly block commands in dms, not globally

## [5.3.0] - 10.12.2024

### Added

- Added `.minesweeper` /  `.mw` command - spoiler-based minesweeper minigame. Just for fun
- Added `.temprole` command - add a role to a user for a certain amount of time, after which the role will be removed
- Added `.xplevelset` - you can now set a level for a user in your server
- Added `.winlb` command - leaderboard of top gambling wins
- Added `.notify` command
    - Specify an event to be notified about, and the bot will post the specified message in the current channel when the
      event occurs
    - A few events supported right now:
        - `UserLevelUp` when user levels up in the server
        - `AddRoleReward` when a role is added to a user through .xpreward system
        - `RemoveRoleReward` when a role is removed from a user through .xpreward system
        - `Protection` when antialt, antiraid or antispam protection is triggered
- Added `.banner` command to see someone's banner
- Selfhosters:
    - Added `.dmmod` and `.dmcmd` - you can now disable or enable whether commands or modules can be executed in bot's
      DMs

### Changed

- Giveaway improvements
    - Now mentions winners in a separate message
    - Shows the timestamp of when the giveaway ends
- Xp Changes
    - Removed awarded xp (the number in the brackets on the xp card)
    - Awarded xp, (or the new level set) now directly apply to user's real xp
    - Server xp notifications are now set by the server admin/manager in a specified channel
- `.sclr show` will now show hex code of the current color
- Queueing a song will now restart the playback if the queue is on the last track and stopped (there were no more tracks
  to play)
- `.translate` will now use 2 embeds instead of 1

### Fixed

- .setstream and .setactivity will now pause .ropl (rotating statuses)
- Fixed `.sar ex` help description

### Removed

- `.xpnotify` command, superseded by `.notify`, although as of right now you can't post user's level up in the same
  channel user last typed, because you have to specify a channel where the notify messages will be posted

## [5.2.4] - 27.11.2024

### Fixed

- More fixes for .sclr
- `.iamn` fixed

## [5.2.3] - 27.11.2024

### Fixed

- `.iam` Fixed
- `.sclr` will now properly change color on many commands it didn't work previously

### Changed

- `.rps` now also has bet amount in the result, like other gambling commands

## [5.2.2] - 27.11.2024

### Changed

- Button roles are now non-exclusive by default

### Fixed

- Fixed sar migration, again (this time correctly)
- Fixed `.sclr` not updating unless bot is restarted, the changes should be immediate now for warn and error
- Fixed group buttons exclusivity message always saying groups are exclusive

## [5.2.1] - 26.11.2024

### Fixed

- Fixed old self assigned missing

## [5.2.0] - 26.11.2024

### Added

- Added `.todo undone` command to unmark a todo as done
- Added Button Roles!
    - `.btr a` to add a button role to the specified message
    - `.btr list` to list all button roles on the server
    - `.btr rm` to remove a button role from the specified message
    - `.btr rma` to remove all button roles on the specified message
    - `.btr excl` to toggle exclusive button roles (only 1 role per message or any number)
    - Use `.h btr` for more info
- Added `.wrongsong` which will delete the last queued song.
    - Useful in case you made a mistake, or the bot queued a wrong song
    - It will reset after a shuffle or fairplay toggle, or similar events.
- Added Server color Commands!
    - Every Server can now set their own colors for ok/error/pending embed (the default green/red/yellow color on the
      left side of the message the bot sends)
    - Use `.h .sclr` to see the list of commands
    - `.sclr show` will show the current server colors
    - `.sclr ok <color hex>` to set ok color
    - `.sclr warn <color hex>` to set warn color
    - `.sclr error <color hex>` to set error color

### Changed

- Self Assigned Roles reworked! Use `.h .sar` for the list of commands
    - `.sar autodel`
        - Toggles the automatic deletion of the user's message and Nadeko's confirmations for .iam and .iamn commands.
    - `.sar ad`
        - Adds a role to the list of self-assignable roles. You can also specify a group.
        - If 'Exclusive self-assignable roles' feature is enabled (.sar exclusive), users will be able to pick one role
          per group.
    - `.sar groupname`
        - Sets a self assignable role group name. Provide no name to remove.
    - `.sar remove`
        - Removes a specified role from the list of self-assignable roles.
    - `.sar list`
        - Lists self-assignable roles. Shows 20 roles per page.
    - `.sar exclusive`
        - Toggles whether self-assigned roles are exclusive. While enabled, users can only have one self-assignable role
          per group.
    - `.sar rolelvlreq`
        - Set a level requirement on a self-assignable role.
    - `.sar grouprolereq`
        - Set a role that users have to have in order to assign a self-assignable role from the specified group.
    - `.sar groupdelete`
        - Deletes a self-assignable role group
    - `.iam` and `.iamn` are unchanged
- Removed patron limits from Reaction Roles. Anyone can have as many reros as they like.
- `.timely` captcha made stronger and cached per user.
- `.bsreset` price reduced by 90%

### Fixed

- Fixed `.sinfo` for servers on other shard

## [5.1.20] - 10.11.2024

### Added

- Added `.rakeback` command, get a % of house edge back as claimable currency
- Added `.snipe` command to quickly get a copy of a posted message as an embed
    - You can reply to a message to snipe that message
    - Or just type .snipe and the bot will snipe the last message in the channel with content or image
- Added `.betstatsreset` / `.bsreset` command to reset your stats for a fee
- Added `.gamblestatsreset` / `.gsreset` owner-only command to reset bot stats for all games
- Added `.waifuclaims` command which lists all of your claimed waifus
- Added and changed `%bot.time%` and `%bot.date%` placeholders. They use timestamp tags now

### Changed

- `.divorce` no longer has a cooldown
- `.betroll` has a 2% better payout
- `.slot` payout balanced out (less volatile), reduced jackpot win but increased other wins,
    - now has a new symbol, wheat
    - worse around 1% in total (now shares the top spot with .bf)

## [5.1.19] - 04.11.2024

### Added

- Added `.betstats`
    - See your own stats with .betstats
    - Target someone else:  .betstats @seraphe
    - You can also specify a game .betstats lula
    - Or both! .betstats seraphe br
- `.timely` can now have a server boost bonus
    - Configure server ids and reward amount in data/gambling.yml
    - anyone who boosts one of the sepcified servers gets the amount as base timely bonus

### Changed

- `.plant/pick` password font size will be slightly bigger
- `.race` will now have 82-94% payout rate based on the number of players playing (1-12, x0.01 per player).
    - Any player over 12 won't increase payout

### Fixed

- `.xplb` and `.xpglb` now have proper ranks after page 1
- Fixed boost bonus on shards different than the specified servers' shard

## [5.1.18] - 02.11.2024

### Added

- Added `.translateflags` / `.trfl` command.
    - Enable on a per-channel basis.
    - Reacting on any message in that channel with a flag emoji will post the translation of that message in the
      language of that country
    - 5 second cooldown per user
    - The message can only be translated once per language (counter resets every 24h)
- `.timely` now has a button. Togglable via `.conf gambling` it's called pass because previously it was a captcha, but
  captchas are too annoying

### Changed

- [public bot] Patreon reward bonus for flowers reduced. Timely bonuses stay the same
- discriminators removed from the databases. All users who had ???? as discriminator have been renamed to ??username.
    - all new unknown users will have ??Unknown as their name
- Flower currency generation will now have a strikeout to try combat the pickbots. This is the weakest but easiest
  protection to implement. There may be more options in the future

### Fixed

- nunchi join game message is now ok color instead of error color

## [5.1.17] - 29.10.2024

### Fixed

- fix: Bot will now not accept .aar Role if that Role is higher than or equal to bot's role. Previously bot would just
  fail silently, now there is a proper error message.

## [5.1.16] - 28.10.2024

## Added

- Added .ncanvas and related commands.
    - You can set pixel colors (and text) on a 500x350 canvas, pepega version of r/place
    - You use currency to set pixels.
    - Commands:
        - see the entire canvas: `.nc`
        - zoom: `.ncz <pos>` or `.ncz x y`
        - set pixel: `.ncsp <pos> <color> <text?>`
        - get pixel: `.ncp <pos>`
    - Owners can use .ncsetimg to set a starting image, use `.h .setimg` for instructions
    - Owners can reset the whole canvas via `.ncreset`

## [5.1.15] - 21.10.2024

### Added

- Added -c option for `.xpglb`
-

### Change

- Leaderboards will now show 10 users per page
- A lot of internal changes and improvements

### Fixed

- Fixed a big issue which caused several features to not get loaded on bot restart
- Alias collision fix `.qse` is now quotesearch, `.qs` will stay `.queuesearch`
- Fixed some migrations which would prevent users from updating from ancient versions
- Waifulb will no longer show #0000 discrims
- More `.greet` command fixes
- Author name will now be counted as content in embeds. Embeds can now only have author fields and still be valid
- Grpc api fixes, and additions

## [5.1.14] - 03.10.2024

### Changed

- Improved `.xplb -c`, it will now correctly only show users who are still in the server with no count limit

### Fixed

- Fixed medusa load error on startup

## [5.1.13] - 03.10.2024

### Fixed

- Grpc api server will no longer start unless enabled in creds
- Seq comment in creds fixed

## [5.1.12] - 03.10.2024

### Added

- Added support for `seq` for logging. If you fill in seq url and apiKey in creds.yml, bot will sends logs to it

### Fixed

- Fixed another bug in `.greet` / `.bye` system, which caused it to show wrong message on a wrong server occasionally

## [5.1.11] - 03.10.2024

### Added

- Added `%user.displayname%` placeholder. It will show users nickname, if there is one, otherwise it will show the
  username.
    - Nickname won't be shown in bye messages.
- Added initial version of grpc api. Beta

### Fixed

- Fixed a bug which caused `.bye` and `.greet` messages to be randomly disabled
- Fixed `.lb -c` breaking sometimes, and fixed pagination

### Changed

- Youtube now always uses `yt-dlp`. Dropped support for `youtube-dl`
    - If you've previously renamed your yt-dlp file to youtube-dl, please rename it back.
- ytProvider in data/searches.yml now also controls where you're getting your song streams from.
    - (Invidious support added for .q)

## [5.1.10] - 24.09.2024

### Fixed

- Fixed claimed waifu decay in `games.yml`

### Changed

- Added some logs for greet service in case there are unforeseen issues, for easier debugging

## [5.1.9] - 21.09.2024

### Fixed

- Fixed `.greettest`, and other `.*test` commands if you didn't have them enabled.
- Fixed `.greetdmtest` sending messages twice.
- Fixed a serious bug which caused greet messages to be jumbled up, and wrong ones to be sent for the wrong events.
    - There is no database issue, all greet messages are safe, the cache was caching any setting every 3 seconds with no
      regard for the type of the event
    - This also caused `.greetdm` messages to not be sent if `.greet` is enabled
    - This bug was introduced in 5.1.8. PLEASE UPDATE if you are on 5.1.8
- Selfhosters only: Fixed medusa dependency loading
    - Note: Make sure to not publish any other DLLs besides the ones you are sure you will need, as there can be version
      conflicts which didn't happen before.

## [5.1.8] - 19.09.2024

### Added

- Added `.leaveunkeptservers` which will make the bot leave all servers on all shards whose owners didn't run `.keep`
  command.
    - This is a dangerous and irreversible command, don't use it. Meant for use on the public bot.
- `.adpl` now supports custom statuses (you no longer need to specify Playing, Watching, etc...)

### Changed

- `.quote` commands cleaned up and improved
    - All quote commands now start with `.q<whatever>` and follow the same naming pattern as Expression commands
    - `.liqu` renamed to `.qli`
    - `.quotesearch` / `.qse` is now paginated for easier searching
- `.whosplaying` is now paginated
- `.img` is now paginated
- `.setgame` renamed to`.setactivity` and now supports custom text activity. You don't have to specify playing,
  listening etc before the activity
- Clarified and added some embed / placeholder links to command help where needed
- dev: A lot of code cleanup and internal improvements

### Fixed

- Fixed `.xpcurrew` breaking xp gain if user gains 0 xp from being in a voice channel
- Fixed a bug in `.gatari` command
- Fixed some waifu related strings
- Fixed `.quoteshow` and `.quoteid` commands
- Fixed some placeholders not working in `.greetdm`
- Fixed postgres support
- Fixed and clarified some command strings/parameter descriptions

### Removed

- Removed mysql support as it didn't work for a while, and requires some special handling/maintenance
    - Sqlite and Postgres support stays

## [5.1.7] - 08.08.2024

### Fixed

- Fixed some command groups incorrectly showing up as modules

## [5.1.6] - 07.08.2024

### Added

- `.serverlist` is now paginated

### Changed

- `.listservers` renamed to `.serverlist`

### Fixed

- `.afk` messages can no longer ping, and the response is moved to DMs to avoid abuse
- Possible fix for `.remind` timestamp

### Removed

- Removed old bloat / semi broken / dumb commands
    - `.memelist` / `.memegen` (too inconvenient to use)
    - `.activity` (useless owner-only command)
    - `.rafflecur` (Just use raffle and then award manually instead)
    - `.rollduel` (we had this command?)
- You can no longer bet on `.connect4`
- `.economy` Removed.
    - Was buggy and didn't really show the real state of the economy.
    - It might come back improved in the future
- `.mal` Removed. Useless information / semi broken

## [5.1.5] - 01.08.2024

### Added

- Added: Added a `.afk <msg>?` command which sets an afk message which will trigger whenever someone pings you
    - Message will when you type a message in any channel that the bot sees, or after 8 hours, whichever comes first
    - The specified message will be prefixed with "The user is afk: "
    - The afk message will disappear 30 seconds after being triggered

### Changed

- Bot now shows a message when .prune fails due to already running error
- Updated some bet descriptions to include 'all' 'half' usage instructions
- Updated some command strings
- dev: Vastly simplified medusa creation using dotnet templates, docs updated
- Slight refactor of .wiki, time, .catfact, .wikia, .define, .bible and .quran commands, no significant change in
  functionality

### Fixed

- .coins will no longer show double minus sign for negative changes
- You can once again disable cleverbot responses using fake 'cleverbot:response' module name in permission commands

### Removed

- Removed .rip command

## [5.1.4] - 13.07.2024

### Added

- Added `.coins` command which lists top 10 cryptos ordered by marketcap
- Added Clubs rank in the leaderboard to `.clubinfo`
- Bot owners can now check other people's bank balance (Not server owners, only bot owner, the person who is hosting the
  bot)
- You can now send multiple waifu gifts at once to waifus. For example `.waifugift 3xRose @user` will give that user 3
  roses
    - The format is `<NUMBER>x<ITEM>`, no spaces
- Added `.boosttest` command
- Added support for any openai compatible api for the chatterbot feature change:
    - Changed games.yml to allow input of the apiUrl (needs to be openai compatible) and modelName as a string.

### Changed

- Updated command strings to clarify `.say` and `.send` usages

### Fixed

- Fixed `.waifugift` help string

### Removed

- Removed selfhost button from `.donate` command, no idea why it was there in the first place

## [5.1.3] - 06.07.2024

### Added

- Added `.quran` command, which will show the provided ayah in english and arabic, including recitation by Alafasy

### Changed

- Replying to the bot's message in the channel where chatterbot is enabled will also trigger the ai response, as if you
  pinged the bot. This only works for chatterbot, but not for nadeko ai command prompts

### Fixed

- Fixed `.stickeradd` it now properly supports 300x300 image uploads.
- Bot should now trim the invalid characters from chatterbot usernames to avoid openai errors
- Fixed prompt triggering chatterbot responses twice

## [5.1.2] - 29.06.2024

### Fixed

- Fixed `.honeypot` not unbanning and not pruning messages

## [5.1.1] - 27.06.2024

### Added

- Added `.honeypot` command, which automatically softbans (ban and immediate unban) any user who posts in that channel.
    - Useful to auto softban bots who spam every channel upon joining
    - Users who run commands or expressions won't be softbanned.
    - Users who have ban member permissions are also excluded.

### Fixed

- Fixed `.betdraw` not respecting maxbet
- Fixed `.xpshop` pagination for real this time?

## [5.1.0] - 25.06.2024

### Added

- Added `.prompt` command, Nadeko Ai Assistant
    - You can send natural language questions, queries or execute commands. For example "@Nadeko how's the weather in
      paris" and it will return `.we Paris` and run it for you.
    - In case the bot can't execute a command using your query, It will fall back to your chatter bot, in case you have
      it enabled in data/games.yml. (Cleverbot or chatgpt)
    - (It's far from perfect so please don't ask the bot to do dangerous things like banning or pruning)
    - Requires Patreon subscription, after which you'll be able to run it on global @Nadeko bot.
        - Selfhosters: If you're selfhosting, you also will need to acquire the api key
          from <https://dashy.nadeko.bot/me> after pledging on patreon and put it in nadekoAiToken in creds.yml
- Added support for `gpt-4o` in `data/games.yml`

### Changed

- Remind will now show a timestamp tag for durations
- Only `Gpt35Turbo` and `Gpt4o` are valid inputs in games.yml now
- `data/patron.yml` changed. It now has limits. The entire feature limit system has been reworked. Your previous
  settings will be reset
- A lot of updates to bot strings (thanks Ene)
- Improved cleanup command to delete a lot more data once cleanup is ran, not only guild configs (please don't use this
  command unless you have your database bakced up and you know 100% what you're doing)

### Fixed

- Fixed xp bg buy button not working, and possibly some other buttons too
- Fixed shopbuy %user% placeholders and updated help text
- All .feed overloads should now work"
- `.xpexclude` should will now work with forums too. If you exclude a forum you won't be able to gain xp in any of the
  threads.
- Fixed remind not showing correct time (thx cata)

### Removed

- Removed PoE related commands
- dev: Removed patron quota data from the database, it will now be stored in redis

## [5.0.8] - 21.05.2024

### Added

- Added `.setserverbanner` and `.setservericon` commands (thx cata)
- Added overloads section to `.h command` which will show you all versions of command usage with param names
- You can now check commands for submodules, for example `.cmds SelfAssignedRoles` will show brief help for each of the
  commands in that submodule
- Added dropdown menus for .mdls and .cmds (both module and group versions) which will give you the option to see more
  detailed help for each specific module, group or command respectively
- Self-Hosters only:
    - Added a dangerous cleanup command that you don't have to know about

### Changed

- Quotes will now use alphanumerical ids (like expressions)

### Fixed

- `.verbose` will now be respected for expression errors
- Using `.pick` will now correctly show the name of the user who picked the currency
- Fixed `.h` not working on some commands
- `.langset` and `.langsetd` should no longer allow unsupported languages and nonsense to be typed in

## [5.0.7] - 15.05.2024

### Fixed

- `.streammessage` will once again be able to mention anyone (as long as the user setting the message has the permission
  to mention everyone)
- `.streammsgall` fixed
- `.xplb` and `.xpglb` pagination fixed
- Fixed page number when the total number of elements is unknown

## [5.0.6] - 14.05.2024

### Changed

- `.greet` and `.bye` will now be automatically disabled if the bot losses permissions to post in the specified channel
- Removed response replies from `.blackjack` and `.pick` as the original message will always be deleted

### Fixed

- Fixed `.blackjack` response string as it contained no user name
- Fixed `.ttt` and `.gift` strings not mentioning the user

## [5.0.5] - 11.05.2024

### Fixed

- `%server.members%` placeholder fixed
- `.say #channel <message>` should now be working properly again
- `.repeat`, `.greet`, `.bye` and `.boost` command can now once again mention anyone

## [5.0.4] - 10.05.2024

### Added

- Added `.shopadd command` You can now sell commands in the shop. The command will execute as if you were the one
  running it when someone buys it
    - type `.h .shopadd` for more info
- Added `.stickyroles` Users leaving the server will have their roles saved to the database and reapplied if they rejoin
  within 30 days.
- Giveaway commands
    - `.ga start <duration> <text>` starts the giveway with the specified duration and message (prize). You may have up
      to 5 giveaways on the server at once
    - `.ga end <id>` prematurely ends the giveaway and selects a winner
    - `.ga cancel <id>` cancels the giveaway and doesn't select a winner
    - `.ga list` lists active giveaways on the current server
    - `.ga reroll <id>` rerolls the winner on the completed giveaway. This only works for 24 hours after the giveaway
      has ended, or until the bot restarts.
    - Users can join the giveaway by adding a :tada: reaction
- Added Todo Commands
    - `.todo add <name>` - adds a new todo
    - `.todo delete <id>` - deletes a todo item
    - `.todo done <id>` - completes a todo (marks it with a checkmark)
    - `.todo list` - lists all todos
    - `.todo edit <id> <new message>` - edits a todo item message
    - `.todo show <id>` - Shows the text of the specified todo item
    - In addition to that, there are also Todo archive commands
        - `.todo archive add <name>` - adds all current todos (completed and not completed) to the archived list, your
          current todo list will become cleared
        - `.todo archive list` - lists all your archived todo lists
        - `.todo archive show <id>` - shows the todo items from one of your archived lists
        - `.todo archive delete <id>` - deletes and archived todo list
- Added `.queufairplay` / `.qfp` (music feature) re-added but it works differently
    - Once you run it, it will reorganize currently queued songs so that they're in a fair order.
- Added `.clubrename` command to uh rename your club
- For self-hosters:
    - Added `.sqlselectcsv` which will return results in a csv file instead of an embed.
    - You can set whether nadeko ignores other bots in `bot.yml`
    - You can set shop sale cut in `gambling.yml`
- Added a page parameter to `.feedlist`
- Added seconds/sec/s to `.convert` command
- Added `.prunecancel` to cancel an active prune
- Added progress reporting when using `.prune`.
- Added audit log reason for `.setrole` and some other features

### Changed

- Users who have manage messages perm in the channel will now be excluded from link and invite filtering (`.sfi`
  and `.sfl`)
- `.send` command should work consistently and correctly now. You can have targets from other shards too. The usage has
  been changed. refer to `.h .send` for more info
- `.serverinfo` no longer takes a server name. It only takes an id or no arguments
- You can now target a different channel with .repeat
- `.cmds <module name>`, `.cmds <group name` and `.mdls` looks better
- The bot will now send a discord Reply to every command
- `.queuesearch` / `.qs` will now show the results with respective video thumbnails
- A lot of code cleanup (still a lot to be done) and Quality of Life improvements
- `.inrole` will now show mentions primarily, and use a spoiler to show usernames

### Fixed

- `.feed` should now correctly accept (and show) the message which can be passed as the third parameter
- `.say` will now correctly report errors if the user or the bot don't have sufficent perms to send a message in the
  targeted channel
- Fixed `.invitelist` not paginating correctly
- `.serverinfo` will now correctly work for other shards
- `.send` will now correctly work for other shards
- `.translate` command will no longer fail if the user capitalizes the language name
- Fixed xp card user avatar not showing for some users

### Removed

- `.poll` commands removed as discord added polls
- `.scpl` and other music soundcloud commands have been removed as soundcloud isn't issuing new api tokens for years now
- Removed a lot of useless and nsfw commands
- Removed log voice presence TTS
- Cleanup: Removed a lot of obsolete aliases from aliases.yml