## Votes Api

This api is used if you want your bot to be able to reward users who vote for it on discords.com or top.gg   

#### [GET] `/discords/new`
    Get the discords votes received after previous call to this endpoint.
    Input full url of this endpoint in your creds.yml file under Discords url field.
    For example "https://api.my.cool.bot/discords/new"
#### [GET] `/topgg/new`
    Get the topgg votes received after previous call to this endpoint.
    Input full url of this endpoint in your creds.yml file under Topgg url field.
    For example "https://api.my.cool.bot/topgg/new"

#### [POST] `/discordswebhook`
    Input this endpoint as the webhook on discords.com bot edit page
    model: https://docs.botsfordiscord.com/methods/receiving-votes
    For example "https://api.my.cool.bot/topggwebhook"
#### [POST] `/topggwebhook`
    Input this endpoint as the webhook https://top.gg/bot/:your-bot-id/webhooks (replace :your-bot-id with your bot's id)
    model: https://docs.top.gg/resources/webhooks/#schema
    For example "https://api.my.cool.bot/discordswebhook"

Input your super-secret header value in appsettings.json's DiscordsKey and TopGGKey fields
They must match your DiscordsKey and TopGG key respectively, as well as your secrets in the discords.com and top.gg webhook setup pages

Full Example:

⚠ Change TopggKey and DiscordsKey to a secure long string  
⚠ You can use https://www.random.org/strings/?num=1&len=20&digits=on&upperalpha=on&loweralpha=on&unique=on&format=html&rnd=new to generate it

`creds.yml`
```yml
votes:
    TopggServiceUrl: "https://api.my.cool.bot/topgg"
    TopggKey: "my_topgg_key"
    DiscordsServiceUrl: "https://api.my.cool.bot/discords"
    DiscordsKey: "my_discords_key"
```

`appsettings.json`
```json
...
  "DiscordsKey": "my_discords_key",
  "TopGGKey": "my_topgg_key",
...
```