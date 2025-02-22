#nullable disable
using NadekoBot.Modules.Searches.Common;
using System.Net.Http.Json;

namespace NadekoBot.Modules.Searches.Services;

public class AnimeSearchService : INService
{
    private readonly IBotCache _cache;
    private readonly IHttpClientFactory _httpFactory;

    public AnimeSearchService(IBotCache cache, IHttpClientFactory httpFactory)
    {
        _cache = cache;
        _httpFactory = httpFactory;
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
            var result = await _cache.GetAsync(GetKey(link));
            if (!result.TryPickT0(out var data, out _))
            {
                using var http = _httpFactory.CreateClient();
                data = await http.GetFromJsonAsync<AnimeResult>(link);

                await _cache.AddAsync(GetKey(link), data, expiry: TimeSpan.FromHours(12));
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
            
            var result = await _cache.GetAsync(GetKey(link));
            if (!result.TryPickT0(out var data, out _))
            {
                using var http = _httpFactory.CreateClient();
                data = await http.GetFromJsonAsync<MangaResult>(link);

                await _cache.AddAsync(GetKey(link), data, expiry: TimeSpan.FromHours(3));
            }


            return data;
        }
        catch
        {
            return null;
        }
    }
}