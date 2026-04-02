#nullable disable
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
        public async Task Anilist([Leftover] string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return;

            var user = await _service.GetAnilistUserAsync(username);

            if (user is null)
            {
                await Response().Error(strs.anilist_user_not_found).SendAsync();
                return;
            }

            var animeStats = user.Statistics?.Anime;
            var mangaStats = user.Statistics?.Manga;

            var watchDays = animeStats is not null ? animeStats.MinutesWatched / 1440 : 0;
            var watchHours = animeStats is not null ? animeStats.MinutesWatched % 1440 / 60 : 0;

            var embed = CreateEmbed()
                .WithOkColor()
                .WithTitle(user.Name)
                .WithUrl(user.SiteUrl)
                .WithThumbnailUrl(user.Avatar?.Large ?? string.Empty)
                .AddField(GetText(strs.anilist_anime_stats),
                    $"""
                    `{GetText(strs.anilist_total_entries)}`
                    {animeStats?.Count ?? 0}
                    `{GetText(strs.episodes)}`
                    {animeStats?.EpisodesWatched ?? 0}
                    `{GetText(strs.anilist_watch_time)}`
                    {watchDays}d {watchHours}h
                    `{GetText(strs.anilist_mean_score)}`
                    {animeStats?.MeanScore ?? 0:0.#}
                    """,
                    true)
                .AddField(GetText(strs.anilist_manga_stats),
                    $"""
                    `{GetText(strs.anilist_total_entries)}`
                    {mangaStats?.Count ?? 0}
                    `{GetText(strs.chapters)}`
                    {mangaStats?.ChaptersRead ?? 0}
                    `{GetText(strs.volumes)}`
                    {mangaStats?.VolumesRead ?? 0}
                    `{GetText(strs.anilist_mean_score)}`
                    {mangaStats?.MeanScore ?? 0:0.#}
                    """,
                    true);
                ;

            if (!string.IsNullOrWhiteSpace(user.About))
            {
                var about = user.About.Length > 300
                    ? string.Concat(user.About.AsSpan(0, 300), "...")
                    : user.About;
                embed.WithDescription(about);
            }

            var favAnime = user.Favourites?.Anime?.Nodes;
            var favManga = user.Favourites?.Manga?.Nodes;
            var favChars = user.Favourites?.Characters?.Nodes;

            var hasFavs = favAnime is { Length: > 0 }
                          || favManga is { Length: > 0 }
                          || favChars is { Length: > 0 };

            if (hasFavs)
            {
                var sb = new System.Text.StringBuilder();

                if (favAnime is { Length: > 0 })
                {
                    sb.AppendLine($"`{GetText(strs.anime)}`");
                    foreach (var node in favAnime)
                        sb.AppendLine($"{node.Title?.English ?? node.Title?.Romaji ?? "?"}");
                }

                if (favManga is { Length: > 0 })
                {
                    sb.AppendLine($"`{GetText(strs.manga)}`");
                    foreach (var node in favManga)
                        sb.AppendLine($"{node.Title?.English ?? node.Title?.Romaji ?? "?"}");
                }

                if (favChars is { Length: > 0 })
                {
                    sb.AppendLine($"`{GetText(strs.characters)}`");
                    foreach (var node in favChars)
                        sb.AppendLine($"{node.Name?.Full ?? "?"}");
                }

                embed.AddField(GetText(strs.anilist_favourites), sb.ToString().TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(user.BannerImage))
                embed.WithImageUrl(user.BannerImage);

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