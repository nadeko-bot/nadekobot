using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Modules.Searches.Common;
using NadekoBot.Modules.Searches.Services;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics.CodeAnalysis;
using Color = SixLabors.ImageSharp.Color;

namespace NadekoBot.Modules.Searches;

public partial class Searches : NadekoModule<SearchesService>
{
    private readonly IBotCreds _creds;
    private readonly IGoogleApiService _google;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly ITimezoneService _tzSvc;

    public Searches(
        IBotCreds creds,
        IGoogleApiService google,
        IHttpClientFactory factory,
        IMemoryCache cache,
        ITimezoneService tzSvc)
    {
        _creds = creds;
        _google = google;
        _httpFactory = factory;
        _cache = cache;
        _tzSvc = tzSvc;
    }

    [Cmd]
    public async Task Weather([Leftover] string query)
    {
        if (!await ValidateQuery(query))
            return;

        var embed = CreateEmbed();
        var data = await _service.GetWeatherDataAsync(query);

        if (data is null)
            embed.WithDescription(GetText(strs.city_not_found)).WithErrorColor();
        else
        {
            var f = StandardConversions.CelsiusToFahrenheit;

            var tz = _tzSvc.GetTimeZoneOrUtc(ctx.Guild?.Id);
            var sunrise = data.Sys.Sunrise.ToUnixTimestamp();
            var sunset = data.Sys.Sunset.ToUnixTimestamp();
            sunrise = sunrise.ToOffset(tz.GetUtcOffset(sunrise));
            sunset = sunset.ToOffset(tz.GetUtcOffset(sunset));
            var timezone = $"UTC{sunrise:zzz}";

            embed
                .AddField("🌍 " + Format.Bold(GetText(strs.location)),
                    $"[{data.Name + ", " + data.Sys.Country}](https://openweathermap.org/city/{data.Id})",
                    true)
                .AddField("📏 " + Format.Bold(GetText(strs.latlong)), $"{data.Coord.Lat}, {data.Coord.Lon}", true)
                .AddField("☁ " + Format.Bold(GetText(strs.condition)),
                    string.Join(", ", data.Weather.Select(w => w.Main)),
                    true)
                .AddField("😓 " + Format.Bold(GetText(strs.humidity)), $"{data.Main.Humidity}%", true)
                .AddField("💨 " + Format.Bold(GetText(strs.wind_speed)), data.Wind.Speed + " m/s", true)
                .AddField("🌡 " + Format.Bold(GetText(strs.temperature)),
                    $"{data.Main.Temp:F1}°C / {f(data.Main.Temp):F1}°F",
                    true)
                .AddField("🔆 " + Format.Bold(GetText(strs.min_max)),
                    $"{data.Main.TempMin:F1}°C - {data.Main.TempMax:F1}°C\n{f(data.Main.TempMin):F1}°F - {f(data.Main.TempMax):F1}°F",
                    true)
                .AddField("🌄 " + Format.Bold(GetText(strs.sunrise)), $"{sunrise:HH:mm} {timezone}", true)
                .AddField("🌇 " + Format.Bold(GetText(strs.sunset)), $"{sunset:HH:mm} {timezone}", true)
                .WithOkColor()
                .WithFooter("Powered by openweathermap.org",
                    $"https://openweathermap.org/img/w/{data.Weather[0].Icon}.png");
        }

        await Response().Embed(embed).SendAsync();
    }

    [Cmd]
    public async Task Time([Leftover] string query)
    {
        if (!await ValidateQuery(query))
            return;

        await ctx.Channel.TriggerTypingAsync();

        var (data, err) = await _service.GetTimeDataAsync(query);
        if (err is not null)
        {
            await HandleErrorAsync(err.Value);
            return;
        }

        if (string.IsNullOrWhiteSpace(data.TimeZoneName))
        {
            await Response().Error(strs.timezone_db_api_key).SendAsync();
            return;
        }

        var eb = CreateEmbed()
                 .WithOkColor()
                 .WithTitle(GetText(strs.time_new))
                 .WithDescription(Format.Code(data.Time.ToString(Culture)))
                 .AddField(GetText(strs.location), string.Join('\n', data.Address.Split(", ")), true)
                 .AddField(GetText(strs.timezone), data.TimeZoneName, true);

        await Response().Embed(eb).SendAsync();
    }

    [Cmd]
    public async Task Movie([Leftover] string query)
    {
        if (!await ValidateQuery(query))
            return;

        await ctx.Channel.TriggerTypingAsync();

        var movie = await _service.GetMovieDataAsync(query);
        if (movie is null)
        {
            await Response().Error(strs.imdb_fail).SendAsync();
            return;
        }

        await Response()
              .Embed(CreateEmbed()
                     .WithOkColor()
                     .WithTitle(movie.Title)
                     .WithUrl($"https://www.imdb.com/title/{movie.ImdbId}/")
                     .WithDescription(movie.Plot.TrimTo(1000))
                     .AddField("Rating", movie.ImdbRating, true)
                     .AddField("Genre", movie.Genre, true)
                     .AddField("Year", movie.Year, true)
                     .WithImageUrl(Uri.IsWellFormedUriString(movie.Poster, UriKind.Absolute)
                         ? movie.Poster
                         : null))
              .SendAsync();
    }

    [Cmd]
    public Task RandomCat()
        => InternalRandomImage(SearchesService.ImageTag.Cats);

    [Cmd]
    public Task RandomDog()
        => InternalRandomImage(SearchesService.ImageTag.Dogs);

    [Cmd]
    public Task RandomFood()
        => InternalRandomImage(SearchesService.ImageTag.Food);

    [Cmd]
    public Task RandomBird()
        => InternalRandomImage(SearchesService.ImageTag.Birds);

    private Task InternalRandomImage(SearchesService.ImageTag tag)
    {
        var url = _service.GetRandomImageUrl(tag);
        return Response().Embed(CreateEmbed().WithOkColor().WithImageUrl(url)).SendAsync();
    }

    [Cmd]
    public async Task Lmgtfy([Leftover] string smh)
    {
        if (!await ValidateQuery(smh))
            return;

        var link = $"https://letmegooglethat.com/?q={Uri.EscapeDataString(smh)}";
        var shortenedUrl = await _service.ShortenLink(link) ?? link;
        await Response().Confirm($"<{shortenedUrl}>").SendAsync();
    }

    [Cmd]
    public async Task Shorten([Leftover] string query)
    {
        if (!await ValidateQuery(query))
            return;

        var shortLink = await _service.ShortenLink(query);

        if (shortLink is null)
        {
            await Response().Error(strs.error_occured).SendAsync();
            return;
        }

        await Response()
              .Embed(CreateEmbed()
                     .WithOkColor()
                     .AddField(GetText(strs.original_url), $"<{query}>")
                     .AddField(GetText(strs.short_url), $"<{shortLink}>"))
              .SendAsync();
    }


    [Cmd]
    public async Task MagicTheGathering([Leftover] string search)
    {
        if (!await ValidateQuery(search))
            return;

        await ctx.Channel.TriggerTypingAsync();
        var card = await _service.GetMtgCardAsync(search);

        if (card is null)
        {
            await Response().Error(strs.card_not_found).SendAsync();
            return;
        }

        var embed = CreateEmbed()
                    .WithOkColor()
                    .WithTitle(card.Name)
                    .WithDescription(card.Description)
                    .WithImageUrl(card.ImageUrl)
                    .AddField(GetText(strs.store_url), card.StoreUrl, true)
                    .AddField(GetText(strs.cost), card.ManaCost, true)
                    .AddField(GetText(strs.types), card.Types, true);

        await Response().Embed(embed).SendAsync();
    }

    [Cmd]
    public async Task Hearthstone([Leftover] string name)
    {
        if (!await ValidateQuery(name))
            return;

        if (string.IsNullOrWhiteSpace(_creds.RapidApiKey))
        {
            await Response().Error(strs.mashape_api_missing).SendAsync();
            return;
        }

        await ctx.Channel.TriggerTypingAsync();
        var card = await _service.GetHearthstoneCardDataAsync(name);

        if (card is null)
        {
            await Response().Error(strs.card_not_found).SendAsync();
            return;
        }

        var embed = CreateEmbed().WithOkColor().WithImageUrl(card.Img);

        if (!string.IsNullOrWhiteSpace(card.Flavor))
            embed.WithDescription(card.Flavor);

        await Response().Embed(embed).SendAsync();
    }

    [Cmd]
    public async Task UrbanDict([Leftover] string query)
    {
        if (!await ValidateQuery(query))
            return;

        await ctx.Channel.TriggerTypingAsync();
        using var http = _httpFactory.CreateClient();
        var res = await http.GetStringAsync($"https://api.urbandictionary.com/v0/define?"
                                            + $"term={Uri.EscapeDataString(query)}");
        var allItems = JsonConvert.DeserializeObject<UrbanResponse>(res)?.List;

        if (allItems is null or { Length: 0 })
        {
            await Response().Error(strs.ud_error).SendAsync();
            return;
        }

        await Response()
              .Paginated()
              .Items(allItems)
              .PageSize(1)
              .CurrentPage(0)
              .Page((items, _) =>
              {
                  var item = items[0];
                  return CreateEmbed()
                         .WithOkColor()
                         .WithUrl(item.Permalink)
                         .WithTitle(item.Word)
                         .WithDescription(item.Definition);
              })
              .SendAsync();
    }

    [Cmd]
    public async Task Define([Leftover] string word)
    {
        if (!await ValidateQuery(word))
            return;


        var maybeItems = await _service.GetDefinitionsAsync(word);

        if (!maybeItems.TryPickT0(out var defs, out var error))
        {
            await HandleErrorAsync(error);
            return;
        }

        await Response()
              .Paginated()
              .Items(defs)
              .PageSize(1)
              .Page((items, _) =>
              {
                  var model = items.First();
                  var embed = CreateEmbed()
                              .WithDescription(ctx.User.Mention)
                              .AddField(GetText(strs.word), model.Word, true)
                              .AddField(GetText(strs._class), model.WordType, true)
                              .AddField(GetText(strs.definition), model.Definition)
                              .WithOkColor();

                  if (!string.IsNullOrWhiteSpace(model.Example))
                      embed.AddField(GetText(strs.example), model.Example);

                  return embed;
              })
              .SendAsync();
    }

    [Cmd]
    public async Task Catfact()
    {
        var maybeFact = await _service.GetCatFactAsync();

        if (!maybeFact.TryPickT0(out var fact, out var error))
        {
            await HandleErrorAsync(error);
            return;
        }

        await Response().Confirm("🐈" + GetText(strs.catfact), fact).SendAsync();
    }

    [Cmd]
    public async Task Wiki([Leftover] string query)
    {
        query = query.Trim();

        if (!await ValidateQuery(query))
            return;

        var maybeRes = await _service.GetWikipediaPageAsync(query);
        if (!maybeRes.TryPickT0(out var res, out var error))
        {
            await HandleErrorAsync(error);
            return;
        }

        var data = res.Data;
        await Response().Text(data.Url).SendAsync();
    }

    public Task<IUserMessage> HandleErrorAsync(ErrorType error)
    {
        var errorKey = error switch
        {
            ErrorType.ApiKeyMissing => strs.api_key_missing,
            ErrorType.InvalidInput => strs.invalid_input,
            ErrorType.NotFound => strs.not_found,
            ErrorType.Unknown => strs.error_occured,
            _ => strs.error_occured,
        };

        return Response().Error(errorKey).SendAsync();
    }

    [Cmd]
    public async Task Color(params Rgba32[] colors)
    {
        if (!colors.Any())
            return;

        var colorObjects = colors.Take(10).ToArray();

        using var img = new Image<Rgba32>(colorObjects.Length * 50, 50);
        for (var i = 0; i < colorObjects.Length; i++)
        {
            var x = i * 50;
            var j = i;
            img.Mutate(m => m.FillPolygon(colorObjects[j], new(x, 0), new(x + 50, 0), new(x + 50, 50), new(x, 50)));
        }

        await using var ms = await img.ToStreamAsync();
        await ctx.Channel.SendFileAsync(ms, "colors.png");
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task Avatar([Leftover] IGuildUser? usr = null)
    {
        usr ??= (IGuildUser)ctx.User;

        var avatarUrl = usr.RealAvatarUrl(2048);

        await Response()
              .Embed(
                  CreateEmbed()
                      .WithOkColor()
                      .AddField("Username", usr.ToString())
                      .AddField("Avatar Url", avatarUrl)
                      .WithThumbnailUrl(avatarUrl.ToString()))
              .SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task Banner([Leftover] IGuildUser? usr = null)
    {
        usr ??= (IGuildUser)ctx.User;

        var bannerUrl = usr.GetGuildBannerUrl(size: 2048)
                        ?? (await ((DiscordSocketClient)ctx.Client).Rest.GetUserAsync(usr.Id))?.GetBannerUrl();

        if (bannerUrl is null)
        {
            await Response()
                  .Error(strs.no_banner)
                  .SendAsync();

            return;
        }

        await Response()
              .Embed(
                  CreateEmbed()
                      .WithOkColor()
                      .AddField("Username", usr.ToString(), true)
                      .AddField("Banner Url", bannerUrl, true)
                      .WithImageUrl(bannerUrl))
              .SendAsync();
    }

    [Cmd]
    public async Task Wikia(string target, [Leftover] string query)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(query))
        {
            await Response().Error(strs.wikia_input_error).SendAsync();
            return;
        }

        var maybeRes = await _service.GetWikiaPageAsync(target, query);

        if (!maybeRes.TryPickT0(out var res, out var error))
        {
            await HandleErrorAsync(error);
            return;
        }

        var response = $"### {res.Title}\n{res.Url}";
        await Response().Text(response).Sanitize().SendAsync();
    }

    [Cmd]
    public async Task Steam([Leftover] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        await ctx.Channel.TriggerTypingAsync();

        var appId = await _service.GetSteamAppIdByName(query);
        if (appId == -1)
        {
            await Response().Error(strs.not_found).SendAsync();
            return;
        }

        await Response().Text($"https://store.steampowered.com/app/{appId}").SendAsync();
    }

    private async Task<bool> ValidateQuery([MaybeNullWhen(false)] string query)
    {
        if (!string.IsNullOrWhiteSpace(query))
            return true;

        await Response().Error(strs.specify_search_params).SendAsync();
        return false;
    }
}