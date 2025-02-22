using NadekoBot.Modules.Searches.Youtube;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    public partial class SearchCommands : NadekoModule
    {
        private readonly ISearchServiceFactory _searchFactory;
        private readonly IBotCache _cache;

        public SearchCommands(
            ISearchServiceFactory searchFactory,
            IBotCache cache)
        {
            _searchFactory = searchFactory;
            _cache = cache;
        }

        [Cmd]
        public async Task Google([Leftover] string? query = null)
        {
            query = query?.Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                await Response().Error(strs.specify_search_params).SendAsync();
                return;
            }

            _ = ctx.Channel.TriggerTypingAsync();

            var search = _searchFactory.GetSearchService();
            var data = await search.SearchAsync(query);

            if (data is null or { Entries: null or { Count: 0 } })
            {
                await Response().Error(strs.no_results).SendAsync();
                return;
            }

            // 3 with an answer
            // 4 without an answer
            // 5 is ideal but it lookes horrible on mobile

            var takeCount = string.IsNullOrWhiteSpace(data.Answer)
                ? 4
                : 3;

            var descStr = data.Entries
                              .Take(takeCount)
                              .Select(static res => $@"**[{Format.Sanitize(res.Title)}]({res.Url})**
*{Format.EscapeUrl(res.DisplayUrl)}*
{Format.Sanitize(res.Description ?? "-")}")
                              .Join("\n\n");

            if (!string.IsNullOrWhiteSpace(data.Answer))
                descStr = Format.Code(data.Answer) + "\n\n" + descStr;

            descStr = descStr.TrimTo(4096);

            var embed = CreateEmbed()
                        .WithOkColor()
                        .WithAuthor(ctx.User)
                        .WithTitle(query.TrimTo(64)!)
                        .WithDescription(descStr)
                        .WithFooter(
                            GetText(strs.results_in(data.Info.TotalResults, data.Info.SearchTime)),
                            "https://i.imgur.com/G46fm8J.png");

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        public async Task Image([Leftover] string query)
        {
            query = query.Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                await Response().Error(strs.specify_search_params).SendAsync();
                return;
            }

            _ = ctx.Channel.TriggerTypingAsync();

            var search = _searchFactory.GetImageSearchService();
            var data = await search.SearchImagesAsync(query);

            if (data is null or { Entries: null or { Count: 0 } })
            {
                await Response().Error(strs.no_search_results).SendAsync();
                return;
            }

            EmbedBuilder CreateImageEmbed(IImageSearchResultEntry entry)
            {
                return CreateEmbed()
                       .WithOkColor()
                       .WithAuthor(ctx.User)
                       .WithTitle(query)
                       .WithImageUrl(entry.Link);
            }

            await Response()
                  .Paginated()
                  .Items(data.Entries)
                  .PageSize(1)
                  .AddFooter(false)
                  .Page((items, _) =>
                  {
                      var item = items.FirstOrDefault();

                      if (item is null)
                          return CreateEmbed()
                                 .WithPendingColor()
                                 .WithDescription(GetText(strs.no_search_results));

                      var embed = CreateImageEmbed(item);

                      return embed;
                  })
                  .SendAsync();
        }

        private TypedKey<string[]> GetYtCacheKey(string query)
            => new($"search:yt:{query}");

        private async Task AddYoutubeUrlToCacheAsync(string query, string[] url)
            => await _cache.AddAsync(GetYtCacheKey(query), url, expiry: 1.Hours());

        private async Task<VideoInfo[]?> GetYoutubeUrlFromCacheAsync(string query)
        {
            var result = await _cache.GetAsync(GetYtCacheKey(query));

            if (!result.TryGetValue(out var urls) || urls.Length == 0)
                return null;

            return urls.Map(url => new VideoInfo()
            {
                Url = url
            });
        }

        [Cmd]
        public async Task Youtube([Leftover] string query)
        {
            query = query.Trim();

            _ = ctx.Channel.TriggerTypingAsync();

            var maybeResults = await GetYoutubeUrlFromCacheAsync(query)
                               ?? await _searchFactory.GetYoutubeSearchService().SearchAsync(query);

            if (maybeResults is not { } result || result.Length == 0)
            {
                await Response().Error(strs.no_results).SendAsync();
                return;
            }

            await AddYoutubeUrlToCacheAsync(query, result.Map(x => x.Url));

            await Response().Text(result[0].Url).SendAsync();
        }

//     [Cmd]
//     public async Task DuckDuckGo([Leftover] string query = null)
//     {
//         query = query?.Trim();
//         if (!await ValidateQuery(query))
//             return;
//
//         _ = ctx.Channel.TriggerTypingAsync();
//
//         var data = await _service.DuckDuckGoSearchAsync(query);
//         if (data is null)
//         {
//             await Response().Error(strs.no_results).SendAsync();
//             return;
//         }
//
//         var desc = data.Results.Take(5)
//                        .Select(res => $@"[**{res.Title}**]({res.Link})
// {res.Text.TrimTo(380 - res.Title.Length - res.Link.Length)}");
//
//         var descStr = string.Join("\n\n", desc);
//
//         var embed = CreateEmbed()
//                        .WithAuthor(ctx.User.ToString(),
//                            "https://upload.wikimedia.org/wikipedia/en/9/90/The_DuckDuckGo_Duck.png")
//                        .WithDescription($"{GetText(strs.search_for)} **{query}**\n\n" + descStr)
//                        .WithOkColor();
//
//         await Response().Embed(embed).SendAsync();
//     }
    }
}