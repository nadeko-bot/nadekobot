#nullable disable
using NadekoBot.Modules.Searches.Common;
using System.Net.Http.Json;

namespace NadekoBot.Modules.Searches.Services;

public sealed class AnimeSearchService(IBotCache cache, IHttpClientFactory httpFactory) : INService
{
    private const string ANILIST_GRAPHQL_URL = "https://graphql.anilist.co";

    private const string ANILIST_USER_QUERY = """
        query ($name: String) {
          User(name: $name) {
            id
            name
            siteUrl
            avatar { large }
            bannerImage
            about
            statistics {
              anime { count meanScore minutesWatched episodesWatched }
              manga { count meanScore chaptersRead volumesRead }
            }
            favourites {
              anime(page: 1, perPage: 2) { nodes { title { romaji english } } }
              manga(page: 1, perPage: 2) { nodes { title { romaji english } } }
              characters(page: 1, perPage: 2) { nodes { name { full } } }
            }
          }
        }
        """;

    public async Task<AnilistUser> GetAnilistUserAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var cacheKey = new TypedKey<AnilistUser>($"anilist_user:{username}");

        var cached = await cache.GetAsync(cacheKey);
        if (cached.TryPickT0(out var cachedUser, out _))
            return cachedUser;

        try
        {
            using var http = httpFactory.CreateClient();
            var response = await http.PostAsJsonAsync(ANILIST_GRAPHQL_URL, new
            {
                query = ANILIST_USER_QUERY,
                variables = new { name = username }
            });

            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync<AnilistUserResponse>();
            var user = result?.Data?.User;

            if (user is not null)
                await cache.AddAsync(cacheKey, user, expiry: TimeSpan.FromHours(1));

            return user;
        }
        catch
        {
            return null;
        }
    }

    public async Task<AnimeResult> GetAnimeData(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentNullException(nameof(query));
        
        TypedKey<AnimeResult> GetKey(string link)
            => new TypedKey<AnimeResult>($"anime2:{link}");
        
        try
        {
            var suffix = Uri.EscapeDataString(query.Replace("/", " ", StringComparison.InvariantCulture));
            var link = $"https://aniapi.nadeko.bot/anime/{suffix}";
            link = link.ToLowerInvariant();
            var result = await cache.GetAsync(GetKey(link));
            if (!result.TryPickT0(out var data, out _))
            {
                using var http = httpFactory.CreateClient();
                data = await http.GetFromJsonAsync<AnimeResult>(link);

                await cache.AddAsync(GetKey(link), data, expiry: TimeSpan.FromHours(12));
            }

            return data;
        }
        catch
        {
            return null;
        }
    }

    public async Task<MangaResult> GetMangaData(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentNullException(nameof(query));
        
        TypedKey<MangaResult> GetKey(string link)
            => new TypedKey<MangaResult>($"manga2:{link}");
        
        try
        {
            var link = "https://aniapi.nadeko.bot/manga/"
                       + Uri.EscapeDataString(query.Replace("/", " ", StringComparison.InvariantCulture));
            link = link.ToLowerInvariant();
            
            var result = await cache.GetAsync(GetKey(link));
            if (!result.TryPickT0(out var data, out _))
            {
                using var http = httpFactory.CreateClient();
                data = await http.GetFromJsonAsync<MangaResult>(link);

                await cache.AddAsync(GetKey(link), data, expiry: TimeSpan.FromHours(3));
            }


            return data;
        }
        catch
        {
            return null;
        }
    }
}