#nullable disable
using NadekoBot.Modules.Searches.Common;
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class OsuCommands : NadekoModule<OsuService>
    {
        private readonly IBotCreds _creds;
        private readonly IHttpClientFactory _httpFactory;

        public OsuCommands(IBotCreds creds, IHttpClientFactory factory)
        {
            _creds = creds;
            _httpFactory = factory;
        }

        [Cmd]
        public async Task Osu(string user, [Leftover] string mode = null)
        {
            if (string.IsNullOrWhiteSpace(user))
                return;

            try
            {
                if (string.IsNullOrWhiteSpace(_creds.OsuApiKey))
                {
                    await Response().Error(strs.osu_api_key).SendAsync();
                    return;
                }

                var obj = await _service.GetOsuData(user, mode);
                if (obj is null)
                {
                    await Response().Error(strs.osu_user_not_found).SendAsync();
                    return;
                }

                var userId = obj.UserId;
                var smode = OsuService.ResolveGameMode(obj.ModeNumber);


                await Response()
                      .Embed(CreateEmbed()
                                    .WithOkColor()
                                    .WithTitle($"osu! {smode} profile for {user}")
                                    .WithThumbnailUrl($"https://a.ppy.sh/{userId}")
                                    .WithDescription($"https://osu.ppy.sh/u/{userId}")
                                    .AddField("Official Rank", $"#{obj.PpRank}", true)
                                    .AddField("Country Rank",
                                        $"#{obj.PpCountryRank} :flag_{obj.Country.ToLower()}:",
                                        true)
                                    .AddField("Total PP", Math.Round(obj.PpRaw, 2), true)
                                    .AddField("Accuracy", Math.Round(obj.Accuracy, 2) + "%", true)
                                    .AddField("Playcount", obj.Playcount, true)
                                    .AddField("Level", Math.Round(obj.Level), true))
                      .SendAsync();
            }
            catch (Exception ex)
            {
                await Response().Error(strs.osu_failed).SendAsync();
                Log.Warning(ex, "Osu command failed");
            }
        }

        [Cmd]
        public async Task Gatari(string user, [Leftover] string mode = null)
        {
            var modeNumber = OsuService.ResolveGameMode(mode);
            var modeStr = OsuService.ResolveGameMode(modeNumber);
            var (userData, userStats) = await _service.GetGatariDataAsync(user, mode);
            if (userStats is null)
            {
                await Response().Error(strs.osu_user_not_found).SendAsync();
                return;
            }

            var embed = CreateEmbed()
                               .WithOkColor()
                               .WithTitle($"osu!Gatari {modeStr} profile for {user}")
                               .WithThumbnailUrl($"https://a.gatari.pw/{userStats.Id}")
                               .WithDescription($"https://osu.gatari.pw/u/{userStats.Id}")
                               .AddField("Official Rank", $"#{userStats.Rank}", true)
                               .AddField("Country Rank",
                                   $"#{userStats.CountryRank} :flag_{userData.Country.ToLower()}:",
                                   true)
                               .AddField("Total PP", userStats.Pp, true)
                               .AddField("Accuracy", $"{Math.Round(userStats.AvgAccuracy, 2)}%", true)
                               .AddField("Playcount", userStats.Playcount, true)
                               .AddField("Level", userStats.Level, true);

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        public async Task Osu5(string user, [Leftover] string mode = null)
        {
            if (string.IsNullOrWhiteSpace(_creds.OsuApiKey))
            {
                await Response().Error("An osu! API key is required.").SendAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(user))
            {
                await Response().Error("Please provide a username.").SendAsync();
                return;
            }
            
            var plays = await _service.GetOsuPlay(user, mode);
            

            var eb = CreateEmbed().WithOkColor().WithTitle($"Top 5 plays for {user}");

            foreach(var (title, desc) in plays)
                eb.AddField(title, desc);

            await Response().Embed(eb).SendAsync();
        }
    }
}