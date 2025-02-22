# Changelog

Mostly based on [keepachangelog](https://keepachangelog.com/en/1.0.0/) except date format. a-c-f-r-o

6.0.0
---

### Added

- Initial version of 6.0.0 release

### Changed

### Fixed


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