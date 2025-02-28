## Setting up your API keys

This part is completely optional, **however it's necessary for music and a few other features to work properly**.

- **GoogleAPIKey**
    - Required for Youtube Song Search, Playlist queuing, and a few more things.
    - Follow these steps on how to setup Google API keys:
        - Go to [Google Console][Google Console] and log in.
        - Create a new project (name does not matter).
        - Once the project is created, go into `Library`
        - Under the `YouTube APIs` section
            - Select `YouTube Data API v3`,
            - Click enable.
        - Search for `Custom Search API`
            - Select `Custom Search API`,
            - Click enable.
        - Open up the `Navigation menu` on the top right with the three lines.
        - select `APIs & Services`, then select `Credentials`,
            - Click `Create Credentials` button,
            - Click on `API Key`
            - A new window will appear with your `Google API key`  
              *NOTE: You don't really need to click on `RESTRICT KEY`, just click on `CLOSE` when you are done.*
            - Copy the key.
        - Open up `creds.yml` and look for `GoogleAPIKey`, paste your API key after the `:`.
        - It should look like this:
        ```yml
        GoogleApiKey: 'AIzaSyDSci1sdlWQOWNVj1vlXxxxxxbk0oWMEzM'
        ```
- **MashapeKey**
    - Required for Hearthstone cards.
    - Api key obtained on https://rapidapi.com (register -> go to MyApps -> Add New App -> Enter Name -> Application key)
    - Copy the key and paste it into `creds.yml`
- **OsuApiKey**
    - Required for Osu commands
    - You can get this key [here](https://osu.ppy.sh/p/api).
- **CleverbotApiKey**
    - Required if you want to use Cleverbot. It's currently a paid service.
    - You can get this key [here](http://www.cleverbot.com/api/).
- **PatreonAccessToken**
    - For Patreon creators only.
- **PatreonCampaignId**
    - For Patreon creators only. Id of your campaign.
- **TwitchClientId and TwitchClientSecret**
    - Mandatory for following twitch streams with `.twitch` (or `.stadd` with twitch link)
    - Go to [apps page](https://dev.twitch.tv/console) on twitch and register your application.
    - You need 2FA enabled on twitch in order to create an application
    - You can set `http://localhost` as the OAuth Redirect URL (and press Add button)
    - Select `Chat Bot` from the Category dropdown
    - Once created, `click Manage`
    - Click `New Secret` and select `OK` in the popup
      **Note: You will need to generate a new Client Secret everytime you exit the page**
    - Copy both to your creds.yml as shown below
    ```yml
        twitchClientId: 516tr61tr1qweqwe86trg3g
        twitchClientSecret: 16tr61tr1q86tweqwe
    ```
- **LocationIqApiKey**
    - Optional. Used only for the `.time` command. https://locationiq.com api key (register and you will receive the token in the email). 
- **TimezoneDbApiKey**
    - Optional. Used only for the `.time` command. https://timezonedb.com api key (register and you will receive the token in the email **YOU HAVE TO ACTIVEATE IT AFTER YOU GET IT**).
- **CoinmarketcapApiKey**
    - Optional. Used only for the `.crypto` command. You can use crypto command without it, but you might get ratelimited from time to time, as all self-hosters share the default api key. https://pro.coinmarketcap.com/

##### Additional Settings

- **TotalShards**
    - Required if the bot will be connected to more than 2500 servers.
    - Most likely unnecessary to change until your bot is added to more than 2500 servers.
- **RedisOptions**
    - Required if the Redis instance is not on localhost or on non-default port.
    - You can find all available options [here](https://stackexchange.github.io/StackExchange.Redis/Configuration.html).
- **RestartCommand**
    - Required if you want to be able to use the `.restart` command
    - If you're using the CLI installer or Linux/OSX, it's easier and more reliable setup Nadeko with auto-restart and just use `.die`

For Windows (Updater), add this to your `creds.yml`

```yml
RestartCommand:
    Cmd: "NadekoBot.exe"
    args: "{0}"
```

For Windows (Source), Linux or OSX, add this to your `creds.yml`

```yml
RestartCommand:
    Cmd: dotnet
    Args: "NadekoBot.dll -- {0}"
```

---

#### End Result

**This is an example of how the `creds.yml` looks like with multiple owners, the restart command (optional) and some of the API keys (also optional):**

```yml
# DO NOT CHANGE
version: 4
# Bot token. Do not share with anyone ever -> https://discordapp.com/developers/applications/
token: 'xxxxxxxxxxxxxxxx.xxxxxx.xxxxxxxxxxxxxxxxxxxxxx'
# List of Ids of the users who have bot owner permissions
# **DO NOT ADD PEOPLE YOU DON'T TRUST**
ownerIds: 
    - 105635123466156544
    - 145521851676884992
    - 341420590009417729
# The number of shards that the bot will running on.
# Leave at 1 if you don't know what you're doing.
totalShards: 1
# Login to https://console.cloud.google.com, create a new project, go to APIs & Services -> Library -> YouTube Data API and enable it.
# Then, go to APIs and Services -> Credentials and click Create credentials -> API key.
# Used only for Youtube Data Api (at the moment).
googleApiKey: 'AIxxxXXxxXxxxxxbk0oWXxxxX'
# Settings for voting system for discordbots. Meant for use on global Nadeko.
votes:
  url: ''
  key: ''
# Patreon auto reward system settings.
# go to https://www.patreon.com/portal -> my clients -> create client
patreon:
# Access token. You have to manually update this 1st of each month by refreshing the token on https://patreon.com/portal
  accessToken: ''
  # Unused atm
  refreshToken: ''
  # Unused atm
  clientSecret: ''
  # Campaign ID of your patreon page. Go to your patreon page (make sure you're logged in) and type "prompt('Campaign ID', window.patreon.bootstrap.creator.data.id);" in the console. (ctrl + shift + i)
  campaignId: ''
# Api key for sending stats to DiscordBotList.
botListToken: ''
# Official cleverbot api key.
cleverbotApiKey: ''
# Redis connection string. Don't change if you don't know what you're doing.
redisOptions: localhost:6379,syncTimeout=30000,responseTimeout=30000,allowAdmin=true,password=
# Database options. Don't change if you don't know what you're doing. Leave null for default values
db:
# Database type. Only sqlite supported atm
  type: sqlite
  # Connection string. Will default to "Data Source=data/NadekoBot.db"
  connectionString: Data Source=data/NadekoBot.db
# Address and port of the coordinator endpoint. Leave empty for default.
# Change only if you've changed the coordinator address or port.
coordinatorUrl: http://localhost:3442
# Api key obtained on https://rapidapi.com (go to MyApps -> Add New App -> Enter Name -> Application key)
rapidApiKey: 4UrKpcWXcxxxxxxxxxxxxxxp1Q8kI6jsn32xxxoVWiY7
# https://locationiq.com api key (register and you will receive the token in the email).
# Used only for .time command.
locationIqApiKey: 
# https://timezonedb.com api key (register and you will receive the token in the email).
# Used only for .time command
timezoneDbApiKey: 
# https://pro.coinmarketcap.com/account/ api key. There is a free plan for personal use.
# Used for cryptocurrency related commands.
coinmarketcapApiKey: 
# Api key used for Osu related commands. Obtain this key at https://osu.ppy.sh/p/api
osuApiKey: 4c8c8fdffdsfdsfsdfsfa33f3f3140a7d93320d6
# Optional Trovo client id.
# You should use this if Trovo stream notifications stopped working or you're getting ratelimit errors.
trovoClientId: 
# Obtain by creating an application at https://dev.twitch.tv/console/apps
twitchClientId: jf2w6kkyrlzfl6mp1b4k25h4jr6b2o
# Obtain by creating an application at https://dev.twitch.tv/console/apps
twitchClientSecret: xXXXxxxxXXx
# Command and args which will be used to restart the bot.
# Only used if bot is executed directly (NOT through the coordinator)
# placeholders: 
#     {0} -> shard id 
#     {1} -> total shards
# Linux default
#     cmd: dotnet
#     args: "NadekoBot.dll -- {0}"
# Windows default
#     cmd: "NadekoBot.exe"
#     args: "{0}"
restartCommand:
  cmd: 
  args: 
```

---

## Database

Nadeko saves all settings and data in the database file `NadekoBot.db`, located in:

- Windows (Updater): `system/data` (can be easily accessed through the `Data` button on the updater)
- Windows (Source), Linux and OSX: `nadekobot/output/data/NadekoBot.db`

In order to open it you will need [SQLite Browser](http://sqlitebrowser.org/).

*NOTE: You don't have to worry if you don't have the `NadekoBot.db` file, it gets automatically created once you successfully run the bot for the first time.*

**To make changes to the database on windows:**

- Shut your bot down.
- Copy the `NadekoBot.db` file to someplace safe. (Back up)
- Open it with SQLite Browser.
- Go to the **Browse Data** tab.
- Click on the **Table** drop-down list.
- Choose the table you want to edit.
- Click on the cell you want to edit.
- Edit it on the right-hand side.
- Click on **Apply**.
- Click on **Write Changes**.

![nadekodb](https://cdn.discordapp.com/attachments/251504306010849280/254067055240806400/nadekodb.gif)

---

## Sharding your bot

To run a sharded bot, you will want to run `src/NadekoBot.Coordinator` project.
Shards communicate with the coordinator using gRPC
To configure your Coordinator, you will need to edit the `src/NadekoBot.Coordinator/coord.yml` file

```yml
# total number of shards
TotalShards: 3
# How often do shards ping their state back to the coordinator
RecheckIntervalMs: 5000
# Command to run the shard
ShardStartCommand: dotnet
# Arguments to run the shard
# {0} = shard id
# {1} = total number of shards
ShardStartArgs: ../../output/NadekoBot.dll -- {0} {1}
# How long does it take for the shard to be forcefully restarted once it stops reporting its state
UnresponsiveSec: 30
```

[Google Console]: https://console.developers.google.com
[DiscordApp]: https://discordapp.com/developers/applications/me
[Invite Guide]: https://tukimoop.pw/s/guide.html
