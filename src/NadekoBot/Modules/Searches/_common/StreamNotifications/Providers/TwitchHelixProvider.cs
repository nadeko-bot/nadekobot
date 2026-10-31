using NadekoBot.Db.Models;
using System.Text.RegularExpressions;
using TwitchLib.Api;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

// public sealed class YoutubeProvide : Provider
// {
//     private readonly IGoogleApiService _api;
//     private readonly IHttpClientFactory _httpFactory;
//
//     public override FollowedStream.FType Platform
//         => FollowedStream.FType.Youtube;
//
//     public YoutubeProvide(IGoogleApiService api, IHttpClientFactory httpFactory)
//     {
//         _api = api;
//         _httpFactory = httpFactory;
//     }
//
//     public override async Task<bool> IsValidUrl(string url)
//     {
//         await Task.Yield();
//         return url.Contains("youtube.com");
//     }
//
//     public override Task<StreamData?> GetStreamDataByUrlAsync(string url)
//     {
//         return default;
//     }
//
//     public override Task<StreamData?> GetStreamDataAsync(string login)
//     {
//         var client = _httpFactory.CreateClient();
//         
//         client.GetAsync()
//         
//         return default;
//     }
//
//     public override Task<IReadOnlyCollection<StreamData>> GetStreamDataAsync(List<string> usernames)
//     {
//         return default;
//     }
// }
//

// todo check client id or whatever
public sealed class TwitchHelixProvider : Provider
{
    private readonly IHttpClientFactory _httpClientFactory;

    private static Regex Regex { get; } = new(@"twitch.tv/(?<name>[\w\d\-_]+)/?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override FollowedStream.FType Platform
        => FollowedStream.FType.Twitch;

    private readonly Lazy<TwitchAPI> _api;
    private readonly string _clientId;
    private bool _warned;

    public TwitchHelixProvider(IHttpClientFactory httpClientFactory, IBotCredsProvider credsProvider)
    {
        _httpClientFactory = httpClientFactory;

        var creds = credsProvider.GetCreds();
        _clientId = creds.TwitchClientId;
        var clientSecret = creds.TwitchClientSecret;
        _api = new(() => new()
        {
            Helix =
            {
                Settings =
                {
                    ClientId = _clientId,
                    Secret = clientSecret
                }
            }
        });
    }

    private async Task<string?> EnsureTokenValidAsync()
        => await _api.Value.Auth.GetAccessTokenAsync();

    public override Task<bool> IsValidUrl(string url)
        => Task.FromResult(Regex.IsMatch(url));

    public override async Task<StreamLookup> LookupByUrlAsync(string url)
    {
        var match = Regex.Match(url);
        if (!match.Success)
            return StreamLookup.NotFound;

        var (data, ok) = await FetchInternalAsync([match.Groups["name"].Value]);
        if (!ok)
            return StreamLookup.Unavailable;

        foreach (var d in data)
            return StreamLookup.Found(d);

        return StreamLookup.NotFound;
    }

    public override async Task<StreamData?> GetStreamDataAsync(string login)
    {
        var (data, _) = await FetchInternalAsync([login]);
        return data.FirstOrDefault();
    }

    public override async Task<IReadOnlyCollection<StreamData>> GetStreamDataAsync(List<string> logins)
        => (await FetchInternalAsync(logins)).Data;

    // Ok is false when Twitch could not answer, so the caller can tell that from a login which does not exist.
    private async Task<(IReadOnlyCollection<StreamData> Data, bool Ok)> FetchInternalAsync(List<string> logins)
    {
        if (logins.Count == 0)
            return ([], true);

        string? token;
        try
        {
            token = await EnsureTokenValidAsync();
        }
        catch (Exception ex)
        {
            Log.Warning("Twitch refused the client id and client secret: {ErrorMessage}", ex.Message);
            return ([], false);
        }

        if (token is null)
        {
            if (!_warned)
            {
                Log.Warning("Twitch client id and client secret key are not added to data/creds.yml or incorrect");
                _warned = true;
            }

            return ([], false);
        }

        using var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Clear();
        http.DefaultRequestHeaders.Add("Client-Id", _clientId);
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var loginsSet = logins.Select(x => x.ToLowerInvariant())
                              .Distinct()
                              .ToHashSet();
        
        var dataDict = new Dictionary<string, StreamData>();
        
        foreach (var chunk in logins.Chunk(100))
        {
            try
            {
                var str = await http.GetStringAsync($"https://api.twitch.tv/helix/users"
                                                    + $"?{chunk.Select(x => $"login={x}").Join('&')}"
                                                    + $"&first=100");

                var resObj = JsonSerializer.Deserialize<HelixUsersResponse>(str);

                if (resObj?.Data is null || resObj.Data.Count == 0)
                    continue;

                foreach (var user in resObj.Data)
                {
                    var lowerLogin = user.Login.ToLowerInvariant();
                    if (loginsSet.Remove(lowerLogin))
                    {
                        dataDict[lowerLogin] = UserToStreamData(user);
                        _failingStreams.TryRemove(lowerLogin, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Something went wrong retreiving {StreamPlatform} streams", Platform);
                return ([], false);
            }
        }
        
        // any item left over loginsSet is an invalid username
        foreach (var login in loginsSet)
        {
            _failingStreams.TryAdd(login, UtcNow);
        }
        
        // only get streams for users which exist
        foreach (var chunk in dataDict.Keys.Chunk(100))
        {
            try
            {
                var str = await http.GetStringAsync($"https://api.twitch.tv/helix/streams"
                                                    + $"?{chunk.Select(x => $"user_login={x}").Join('&')}"
                                                    + "&first=100");

                var res = JsonSerializer.Deserialize<HelixStreamsResponse>(str);

                if (res?.Data is null || res.Data.Count == 0)
                {
                    continue;
                }

                foreach (var helixStreamData in res.Data)
                {
                    var login = helixStreamData.UserLogin.ToLowerInvariant();
                    if (dataDict.TryGetValue(login, out var old))
                    {
                        dataDict[login] = FillStreamData(old, helixStreamData);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Something went wrong retreiving {StreamPlatform} streams", Platform);
                return ([], false);
            }
        }

        return (dataDict.Values, true);
    }

    private StreamData UserToStreamData(HelixUsersResponse.User user)
        => new()
        {
            UniqueName = user.Login,
            Name = user.DisplayName,
            AvatarUrl = user.ProfileImageUrl,
            IsLive = false,
            StreamUrl = $"https://twitch.tv/{user.Login}",
            StreamType = FollowedStream.FType.Twitch,
            Preview = user.OfflineImageUrl
        };
    
    private StreamData FillStreamData(StreamData partial, HelixStreamsResponse.StreamData apiData)
        => partial with
        {
            StreamType = FollowedStream.FType.Twitch,
            Viewers = apiData.ViewerCount,
            Title = apiData.Title,
            IsLive = apiData.Type == "live",
            Preview = apiData.ThumbnailUrl
                             .Replace("{width}", "640")
                             .Replace("{height}", "480"),
            Game = apiData.GameName,
        };
}