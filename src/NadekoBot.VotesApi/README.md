## Votes Api

Rewards users who vote for the bot on top.gg, discordbotlist.com or discords.com.
The vote sites call the webhooks below. VotesApi checks the key of each site, then gives the vote to
NadekoBot.Coordinator (`POST /coord/votes`). Shard 0 takes the votes from the coordinator and gives the reward
(`voteReward` in `gambling.yml`). A bot without the coordinator gives no vote rewards.

#### [POST] `/topggwebhook`
    Set it as the webhook on https://top.gg/bot/:your-bot-id/webhooks, with TopGGKey as the authorization.
#### [POST] `/discordbotlistwebhook`
    Set it as the webhook on discordbotlist.com, with DiscordBotListKey as the authorization.
#### [POST] `/discordswebhook`
    Set it as the webhook on the discords.com bot edit page, with DiscordsKey as the authorization.

Test webhooks (top.gg type `test`, discords.com types other than `vote`) answer `200` and give no reward.

When the coordinator does not take the vote (it restarts, or 1,000 votes wait), the webhook answers `503`.
A vote site which retries then sends it again later. A vote which the coordinator refuses as not valid answers `200`.

`appsettings.json` (or environment variables of the same names):
```json
...
  "DiscordsKey": "my_discords_key",
  "TopGGKey": "my_topgg_key",
  "DiscordBotListKey": "my_discordbotlist_key",
  "CoordinatorUrl": "http://localhost:3442",
...
```

- `CoordinatorUrl` is the url of the coordinator's shard port (default `http://localhost:3442`). It must match the
  url in the coordinator's `appsettings.json`, and it must start with `http://`. That port has no key and stays on
  loopback, so run VotesApi on the same machine.
- Use long random strings for the three site keys.

#### Run

1. Build it: `dotnet publish src/NadekoBot.VotesApi -c Release -o <folder>`.
2. Put the three site keys in `appsettings.json` in that folder. The build has an `appsettings.json` with example keys, so do not copy it over yours when you update.
3. Start it on the machine of the coordinator, under a supervisor such as systemd, for example
   `./NadekoBot.VotesApi --urls http://127.0.0.1:<port>`.
4. Put a reverse proxy with TLS in front of it, and give its webhook urls to the vote sites. Use `0.0.0.0` in
   `--urls` only when the proxy runs on another machine.
5. The coordinator must run. Without it, every webhook answers `503`.

In Docker, `localhost` is the container and not the machine, so VotesApi cannot reach the coordinator.
Start the container with `--network host` (Linux), or run VotesApi without Docker. Do not open the coordinator port to
other machines to work around this: its routes have no key.
