using NadekoBot.Modules.Games.Fish;
using NadekoBot.Modules.Xp.Services;
using Format = Discord.Format;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    public class FishingCommands(
        FishService fs,
        FishItemService fis,
        FishConfigService fcs,
        IBotCache cache,
        UserService us,
        CaptchaService captchaService) : NadekoModule
    {
        private static readonly NadekoRandom _rng = new();

        private TypedKey<bool> FishingWhitelistKey(ulong userId)
            => new($"fishingwhitelist:{userId}");

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Fish()
        {
            var cRes = await cache.GetAsync(FishingWhitelistKey(ctx.User.Id));
            if (cRes.TryPickT1(out _, out _))
            {
                string? password = null;
                if (fcs.Data.RequireCaptcha)
                    password = await captchaService.GetUserCaptcha(ctx.User.Id);

                if (password is not null)
                {
                    var img = captchaService.GetPasswordImage(password);
                    using var stream = await img.ToStreamAsync();

                    var toSend = Response()
                        .File(stream, "timely.png")
                        .Embed(CreateEmbed()
                            .WithFooter("captcha: type the text from the image")
                            .WithImageUrl("attachment://timely.png"));

#if GLOBAL_NADEKO
                    if (_rng.Next(0, 8) == 0)
                        toSend = toSend
                            .Text("*[Sub on Patreon](https://patreon.com/nadekobot) to remove captcha.*");
#endif
                    var captcha = await toSend.SendAsync();

                    try
                    {
                        var userInput = await GetUserInputAsync(ctx.User.Id, ctx.Channel.Id);
                        if (userInput?.ToLowerInvariant() != password?.ToLowerInvariant())
                        {
                            return;
                        }

                        // whitelist the user for 30 minutes
                        await cache.AddAsync(FishingWhitelistKey(ctx.User.Id), true, TimeSpan.FromMinutes(30));
                        // reset the password
                        await captchaService.ClearUserCaptcha(ctx.User.Id);
                    }
                    finally
                    {
                        _ = captcha.DeleteAsync();
                    }
                }
            }


            var multis = await fis.GetUserMultipliersAsync(ctx.User.Id);
            var fishResult = await fs.FishAsync(ctx.User.Id, ctx.Channel.Id, multis);
            if (fishResult.TryPickT1(out _, out var fishTask))
            {
                return;
            }


            var currentWeather = fs.GetCurrentWeather();
            var currentTod = fs.GetTime();
            var spot = fs.GetSpot(ctx.Channel.Id);

            var msg = await Response()
                .Embed(CreateEmbed()
                    .WithPendingColor()
                    .WithAuthor(ctx.User)
                    .WithDescription($"""
                                      {GetText(strs.fish_waiting)}
                                      {FishItemCommands.GetMultiplierInfo(multis)}
                                      """)
                    .AddField(GetText(strs.fish_spot), GetSpotEmoji(spot) + " " + spot.ToString(), true)
                    .AddField(GetText(strs.fish_weather),
                        GetWeatherEmoji(currentWeather) + " " + currentWeather,
                        true)
                    .AddField(GetText(strs.fish_tod), GetTodEmoji(currentTod) + " " + currentTod, true))
                .SendAsync();

            var res = await fishTask;
            if (res is null)
            {
                await Response().Error(strs.fish_nothing).SendAsync();
                return;
            }

            var desc = GetText(strs.fish_caught(res.Fish.Emoji + " " + Format.Bold(res.Fish.Name)));

            if (res.IsSkillUp)
            {
                desc += "\n" + GetText(strs.fish_skill_up(res.Skill, res.MaxSkill));
            }

            await Response()
                .Embed(CreateEmbed()
                    .WithOkColor()
                    .WithAuthor(ctx.User)
                    .WithDescription(desc)
                    .AddField(GetText(strs.fish_quality), fs.GetStarText(res.Stars, res.Fish.Stars), true)
                    .AddField(GetText(strs.desc), res.Fish.Fluff, true)
                    .WithThumbnailUrl(res.Fish.Image))
                .SendAsync();

            await msg.DeleteAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FishSpot()
        {
            var ws = fs.GetWeatherForPeriods(7);
            var spot = fs.GetSpot(ctx.Channel.Id);
            var time = fs.GetTime();

            await Response()
                .Embed(CreateEmbed()
                    .WithOkColor()
                    .WithDescription(GetText(strs.fish_weather_duration(fs.GetWeatherPeriodDuration())))
                    .AddField(GetText(strs.fish_spot), GetSpotEmoji(spot) + " " + spot, true)
                    .AddField(GetText(strs.fish_tod), GetTodEmoji(time) + " " + time, true)
                    .AddField(GetText(strs.fish_weather_forecast),
                        ws.Select(x => GetWeatherEmoji(x)).Join(""),
                        true))
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FishList(int page = 1)
        {
            if (--page < 0)
                return;

            var fishes = await fs.GetAllFish();

            var catches = await fs.GetUserCatches(ctx.User.Id);
            var (skill, maxSkill) = await fs.GetSkill(ctx.User.Id);

            var catchDict = catches.ToDictionary(x => x.FishId, x => x);

            var items = await fis.GetEquippedItemsAsync(ctx.User.Id);
            var desc = $"""
                        🧠 {skill} / {maxSkill}
                        """;

            foreach (var itemType in Enum.GetValues<FishItemType>())
            {
                var i = items.Where(x => x.Item.ItemType == itemType).ToArray();

                desc += " · " + FishItemCommands.GetEmoji(itemType) + " " +
                        (i.Any() ? string.Join(", ", i.Select(x => x.Item.Name)) : "None");
            }

            await Response()
                .Paginated()
                .Items(fishes)
                .PageSize(9)
                .CurrentPage(page)
                .Page((pageFish, i) =>
                {
                    var eb = CreateEmbed()
                        .WithDescription(desc)
                        .WithAuthor(ctx.User)
                        .WithTitle(GetText(strs.fish_list_title))
                        .WithOkColor();

                    foreach (var f in pageFish)
                    {
                        if (catchDict.TryGetValue(f.Id, out var c))
                        {
                            eb.AddField(f.Name,
                                GetFishEmoji(f, c.Count)
                                + " "
                                + GetSpotEmoji(f.Spot)
                                + GetTodEmoji(f.Time)
                                + GetWeatherEmoji(f.Weather)
                                + "\n"
                                + fs.GetStarText(c.MaxStars, f.Stars)
                                + "\n"
                                + Format.Italics(f.Fluff),
                                true);
                        }
                        else
                        {
                            eb.AddField("?", GetFishEmoji(null, 0) + "\n" + fs.GetStarText(0, f.Stars), true);
                        }
                    }

                    return eb;
                })
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FishLb(int page = 1)
        {
            if (--page < 0)
                return;

            await Response()
                .Paginated()
                .PageItems(async p => await fs.GetFishLbAsync(p))
                .PageSize(9)
                .Page(async (items, page) =>
                {
                    var users = await us.GetUsersAsync(items.Select(x => x.UserId).ToArray());

                    var eb = CreateEmbed()
                        .WithTitle(GetText(strs.fish_lb_title))
                        .WithOkColor();

                    for (var i = 0; i < items.Count; i++)
                    {
                        var data = items[i];
                        var user = users.TryGetValue(data.UserId, out var ud)
                            ? ud.ToString()
                            : data.UserId.ToString();

                        eb.AddField("#" + (page * 9 + i + 1) + " | " + user,
                            GetText(strs.fish_catches(Format.Bold(data.Catches.ToString()))),
                            false);
                    }

                    return eb;
                })
                .SendAsync();
        }


        private string GetFishEmoji(FishData? fish, int count)
        {
            if (fish is null)
                return "";

            return fish.Emoji + " x" + count;
        }

        private string GetSpotEmoji(FishingSpot? spot)
        {
            if (spot is not FishingSpot fs)
                return string.Empty;

            var conf = fcs.Data;

            return conf.SpotEmojis[(int)fs];
        }

        private string GetTodEmoji(FishingTime? fishTod)
        {
            return fishTod switch
            {
                FishingTime.Night => "🌑",
                FishingTime.Dawn => "🌅",
                FishingTime.Dusk => "🌆",
                FishingTime.Day => "☀️",
                _ => ""
            };
        }

        private string GetWeatherEmoji(FishingWeather? w)
            => w switch
            {
                FishingWeather.Rain => "🌧️",
                FishingWeather.Snow => "❄️",
                FishingWeather.Storm => "⛈️",
                FishingWeather.Clear => "☀️",
                _ => ""
            };
    }
}

public enum FishingSpot
{
    Ocean,
    River,
    Lake,
    Swamp,
    Reef
}

public enum FishingTime
{
    Night,
    Dawn,
    Day,
    Dusk
}

public enum FishingWeather
{
    Clear,
    Rain,
    Storm,
    Snow
}