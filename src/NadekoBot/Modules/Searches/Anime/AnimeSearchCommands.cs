#nullable disable
using AngleSharp;
using AngleSharp.Html.Dom;
using NadekoBot.Modules.Searches.Services;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class AnimeSearchCommands : NadekoModule<AnimeSearchService>
    {
        [Cmd]
        public async Task Anime([Leftover] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return;

            var animeData = await _service.GetAnimeData(query);

            if (animeData is null)
            {
                await Response().Error(strs.failed_finding_anime).SendAsync();
                return;
            }

            var embed = CreateEmbed()
                               .WithOkColor()
                               .WithDescription(animeData.Synopsis.Replace("<br>",
                                   Environment.NewLine,
                                   StringComparison.InvariantCulture))
                               .WithTitle(animeData.TitleEnglish)
                               .WithUrl(animeData.Link)
                               .WithImageUrl(animeData.ImageUrlLarge)
                               .AddField(GetText(strs.episodes), animeData.TotalEpisodes.ToString(), true)
                               .AddField(GetText(strs.status), animeData.AiringStatus, true)
                               .AddField(GetText(strs.genres),
                                   string.Join(",\n", animeData.Genres.Any() ? animeData.Genres : ["none"]),
                                   true)
                               .WithFooter($"{GetText(strs.score)} {animeData.AverageScore} / 100");
            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Manga([Leftover] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return;

            var mangaData = await _service.GetMangaData(query);

            if (mangaData is null)
            {
                await Response().Error(strs.failed_finding_manga).SendAsync();
                return;
            }

            var embed = CreateEmbed()
                               .WithOkColor()
                               .WithDescription(mangaData.Synopsis.Replace("<br>",
                                   Environment.NewLine,
                                   StringComparison.InvariantCulture))
                               .WithTitle(mangaData.TitleEnglish)
                               .WithUrl(mangaData.Link)
                               .WithImageUrl(mangaData.ImageUrlLge)
                               .AddField(GetText(strs.chapters), mangaData.TotalChapters.ToString(), true)
                               .AddField(GetText(strs.status), mangaData.PublishingStatus, true)
                               .AddField(GetText(strs.genres),
                                   string.Join(",\n", mangaData.Genres.Any() ? mangaData.Genres : ["none"]),
                                   true)
                               .WithFooter($"{GetText(strs.score)} {mangaData.AverageScore} / 100");

            await Response().Embed(embed).SendAsync();
        }
    }
}