#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class XkcdCommands : NadekoModule
    {
        private const string XKCD_URL = "https://xkcd.com";
        private readonly IHttpClientFactory _httpFactory;

        public XkcdCommands(IHttpClientFactory factory)
            => _httpFactory = factory;

        [Cmd]
        [Priority(0)]
        public async Task Xkcd(string arg = null)
        {
            if (arg?.ToLowerInvariant().Trim() == "latest")
            {
                try
                {
                    using var http = _httpFactory.CreateClient();
                    var res = await http.GetStringAsync($"{XKCD_URL}/info.0.json");
                    var comic = JsonConvert.DeserializeObject<XkcdComic>(res);
                    var embed = CreateEmbed()
                                   .WithOkColor()
                                   .WithImageUrl(comic.ImageLink)
                                   .WithAuthor(comic.Title, "https://xkcd.com/s/919f27.ico", $"{XKCD_URL}/{comic.Num}")
                                   .AddField(GetText(strs.comic_number), comic.Num.ToString(), true)
                                   .AddField(GetText(strs.date), $"{comic.Month}/{comic.Year}", true);
                    var sent = await Response().Embed(embed).SendAsync();

                    await Task.Delay(10000);

                    await sent.ModifyAsync(m => m.Embed = embed.AddField("Alt", comic.Alt).Build());
                }
                catch (HttpRequestException)
                {
                    await Response().Error(strs.comic_not_found).SendAsync();
                }

                return;
            }

            await Xkcd(new NadekoRandom().Next(1, 1750));
        }

        [Cmd]
        [Priority(1)]
        public async Task Xkcd(int num)
        {
            if (num < 1)
                return;
            try
            {
                using var http = _httpFactory.CreateClient();
                var res = await http.GetStringAsync($"{XKCD_URL}/{num}/info.0.json");

                var comic = JsonConvert.DeserializeObject<XkcdComic>(res);
                var embed = CreateEmbed()
                               .WithOkColor()
                               .WithImageUrl(comic.ImageLink)
                               .WithAuthor(comic.Title, "https://xkcd.com/s/919f27.ico", $"{XKCD_URL}/{num}")
                               .AddField(GetText(strs.comic_number), comic.Num.ToString(), true)
                               .AddField(GetText(strs.date), $"{comic.Month}/{comic.Year}", true);

                var sent = await Response().Embed(embed).SendAsync();

                await Task.Delay(10000);

                await sent.ModifyAsync(m => m.Embed = embed.AddField("Alt", comic.Alt).Build());
            }
            catch (HttpRequestException)
            {
                await Response().Error(strs.comic_not_found).SendAsync();
            }
        }
    }

    public class XkcdComic
    {
        public int Num { get; set; }
        public string Month { get; set; }
        public string Year { get; set; }

        [JsonProperty("safe_title")]
        public string Title { get; set; }

        [JsonProperty("img")]
        public string ImageLink { get; set; }

        public string Alt { get; set; }
    }
}