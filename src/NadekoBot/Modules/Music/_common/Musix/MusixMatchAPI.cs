using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Caching.Memory;
using Musix.Models;

// All credit goes to https://github.com/Strvm/musicxmatch-api for the original implementation
namespace Musix
{
    public sealed class MusixMatchAPI
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://www.musixmatch.com/ws/1.1/";

        private readonly string _userAgent =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36";

        private readonly IMemoryCache _cache;
        private readonly JsonSerializerOptions _jsonOptions;

        public MusixMatchAPI(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
            _httpClient.DefaultRequestHeaders.Add("Cookie", "mxm_bab=AB");

            _jsonOptions = new()
            {
                PropertyNameCaseInsensitive = true
            };
            _cache = new MemoryCache(new MemoryCacheOptions { });
        }

        private async Task<string> GetLatestAppUrlAsync()
        {
            var url = "https://www.musixmatch.com/search";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            request.Headers.Add("Cookie", "mxm_bab=AB");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var htmlContent = await response.Content.ReadAsStringAsync();

            var pattern = @"src=""([^""]*/_next/static/chunks/pages/_app-[^""]+\.js)""";
            var matches = Regex.Matches(htmlContent, pattern);

            return matches.Count > 0
                ? matches[^1].Groups[1].Value
                : throw new("_app URL not found in the HTML content.");
        }

        private async Task<string> GetSecret()
        {
            var latestAppUrl = await GetLatestAppUrlAsync();
            var response = await _httpClient.GetAsync(latestAppUrl);
            response.EnsureSuccessStatusCode();
            var javascriptCode = await response.Content.ReadAsStringAsync();

            var pattern = @"from\(\s*""(.*?)""\s*\.split";
            var match = Regex.Match(javascriptCode, pattern);

            if (match.Success)
            {
                var encodedString = match.Groups[1].Value;
                var reversedString = new string(encodedString.Reverse().ToArray());
                var decodedBytes = Convert.FromBase64String(reversedString);
                return Encoding.UTF8.GetString(decodedBytes);
            }

            throw new Exception("Encoded string not found in the JavaScript code.");
        }

        // It seems this is required in order to have multiword queries.
        // Spaces don't work in the original implementation either
        private string UrlEncode(string value)
            => HttpUtility.UrlEncode(value)
                .Replace("+", "-");

        private async Task<string> GenerateSignature(string url)
        {
            var currentDate = DateTime.Now;
            var l = currentDate.Year.ToString();
            var s = currentDate.Month.ToString("D2");
            var r = currentDate.Day.ToString("D2");

            var message = (url + l + s + r);
            var secret = await _cache.GetOrCreateAsync("secret", async _ => await GetSecret());
            var key = Encoding.UTF8.GetBytes(secret ?? string.Empty);
            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var hmac = new HMACSHA256(key);
            var hashBytes = hmac.ComputeHash(messageBytes);
            var signature = Convert.ToBase64String(hashBytes);
            return $"&signature={UrlEncode(signature)}&signature_protocol=sha256";
        }

        public async Task<MusixMatchResponse<TrackSearchResponse>> SearchTracksAsync(string trackQuery, int page = 1)
        {
            var endpoint =
                $"track.search?app_id=community-app-v1.0&format=json&q={UrlEncode(trackQuery)}&f_has_lyrics=true&page_size=100&page={page}";
            var jsonResponse = await MakeRequestAsync(endpoint);
            return JsonSerializer.Deserialize<MusixMatchResponse<TrackSearchResponse>>(jsonResponse, _jsonOptions)
                   ?? throw new JsonException("Failed to deserialize track search response");
        }

        public async Task<MusixMatchResponse<LyricsResponse>> GetTrackLyricsAsync(int trackId)
        {
            var endpoint = $"track.lyrics.get?app_id=community-app-v1.0&format=json&track_id={trackId}";
            var jsonResponse = await MakeRequestAsync(endpoint);
            return JsonSerializer.Deserialize<MusixMatchResponse<LyricsResponse>>(jsonResponse, _jsonOptions)
                   ?? throw new JsonException("Failed to deserialize lyrics response");
        }

        private async Task<string> MakeRequestAsync(string endpoint)
        {
            var fullUrl = _baseUrl + endpoint;
            var signedUrl = fullUrl + await GenerateSignature(fullUrl);

            var request = new HttpRequestMessage(HttpMethod.Get, signedUrl);
            request.Headers.UserAgent.ParseAdd(_userAgent);
            request.Headers.Add("Cookie", "mxm_bab=AB");

            var response = await _httpClient.SendAsync(request);

            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Error in musix api request. Status: {ResponseStatusCode}, Content: {Content}",
                    response.StatusCode,
                    content);
                response.EnsureSuccessStatusCode(); // This will throw with the appropriate status code
            }

            return content;
        }
    }
}