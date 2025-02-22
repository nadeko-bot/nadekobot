using NadekoBot.Db.Models;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

public class TrovoProvider : Provider
{
    private readonly IHttpClientFactory _httpClientFactory;

    public override FollowedStream.FType Platform
        => FollowedStream.FType.Trovo;

    private readonly Regex _urlRegex = new(@"trovo.live\/(?<channel>[\w\d\-_]+)/?", RegexOptions.Compiled);

    private readonly IBotCredsProvider _creds;


    public TrovoProvider(IHttpClientFactory httpClientFactory, IBotCredsProvider creds)
    {
        (_httpClientFactory, _creds) = (httpClientFactory, creds);


        if (string.IsNullOrWhiteSpace(creds.GetCreds().TrovoClientId))
        {
            Log.Warning("""
                Trovo streams are using a default clientId.
                If you are experiencing ratelimits, you should create your own application at: https://developer.trovo.live/
                """);
        }
    }

    public override Task<bool> IsValidUrl(string url)
        => Task.FromResult(_urlRegex.IsMatch(url));

    public override Task<StreamData?> GetStreamDataByUrlAsync(string url)
    {
        var match = _urlRegex.Match(url);
        if (match.Length == 0)
            return Task.FromResult(default(StreamData?));

        return GetStreamDataAsync(match.Groups["channel"].Value);
    }

    public override async Task<StreamData?> GetStreamDataAsync(string login)
    {
        using var http = _httpClientFactory.CreateClient();

        var trovoClientId = _creds.GetCreds().TrovoClientId;

        if (string.IsNullOrWhiteSpace(trovoClientId))
        {
            trovoClientId = "8b3cc4719b7051803099661a3265e50b";
        }

        http.DefaultRequestHeaders.Clear();
        http.DefaultRequestHeaders.Add("Accept", "application/json");
        http.DefaultRequestHeaders.Add("Client-ID", trovoClientId);

        // trovo ratelimit is very generous (1200 per minute)
        // so there is no need for ratelimit checks atm
        try
        {
            using var res = await http.PostAsJsonAsync(
                $"https://open-api.trovo.live/openplatform/channels/id",
                new TrovoRequestData()
                {
                    Username = login
                });

            res.EnsureSuccessStatusCode();

            var data = await res.Content.ReadFromJsonAsync<TrovoGetUsersResponse>();

            if (data is null)
            {
                Log.Warning("An empty response received while retrieving stream data for trovo.live/{TrovoId}", login);
                _failingStreams.TryAdd(login, DateTime.UtcNow);
                return null;
            }

            _failingStreams.TryRemove(data.Username, out _);
            return new()
            {
                IsLive = data.IsLive,
                Game = data.CategoryName,
                Name = data.Username,
                Title = data.LiveTitle,
                Viewers = data.CurrentViewers,
                AvatarUrl = data.ProfilePic,
                StreamType = Platform,
                StreamUrl = data.ChannelUrl,
                UniqueName = data.Username,
                Preview = data.Thumbnail
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error retrieving stream data for trovo.live/{TrovoId}", login);
            _failingStreams.TryAdd(login, DateTime.UtcNow);
            return null;
        }
    }

    public override async Task<IReadOnlyCollection<StreamData>> GetStreamDataAsync(List<string> usernames)
    {
        var trovoClientId = _creds.GetCreds().TrovoClientId;
        
        if (string.IsNullOrWhiteSpace(trovoClientId))
        {
            Log.Warning("Trovo streams will be ignored until TrovoClientId is added to creds.yml");
            return Array.Empty<StreamData>();
        }
        
        var results = new List<StreamData>(usernames.Count);
        foreach (var chunk in usernames.Chunk(10)
                                       .Select(x => x.Select(GetStreamDataAsync)))
        {
            var chunkResults = await Task.WhenAll(chunk);
            results.AddRange(chunkResults.Where(x => x is not null)!);
            await Task.Delay(1000);
        }

        return results;
    }
}