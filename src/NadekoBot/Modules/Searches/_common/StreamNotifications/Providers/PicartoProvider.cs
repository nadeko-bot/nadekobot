using NadekoBot.Db.Models;
using Newtonsoft.Json;
using System.Net;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

public class PicartoProvider : Provider
{
    private static Regex Regex { get; } = new(@"picarto.tv/(?<name>.+[^/])/?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public override FollowedStream.FType Platform
        => FollowedStream.FType.Picarto;

    private readonly IHttpClientFactory _httpClientFactory;

    public PicartoProvider(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public override Task<bool> IsValidUrl(string url)
        => Task.FromResult(Regex.IsMatch(url));

    public override async Task<StreamLookup> LookupByUrlAsync(string url)
    {
        var match = Regex.Match(url);
        if (!match.Success)
            return StreamLookup.NotFound;

        using var http = CreateClientInternal();
        return await FetchInternalAsync(http, match.Groups["name"].Value);
    }

    public override async Task<StreamData?> GetStreamDataAsync(string login)
    {
        var data = await GetStreamDataAsync([login]);

        return data.FirstOrDefault();
    }

    public override async Task<IReadOnlyCollection<StreamData>> GetStreamDataAsync(List<string> logins)
    {
        if (logins.Count == 0)
            return [];

        using var http = CreateClientInternal();
        var toReturn = new List<StreamData>();
        foreach (var login in logins)
        {
            var lookup = await FetchInternalAsync(http, login);
            if (lookup.Data is { } data)
                toReturn.Add(data);
        }

        return toReturn;
    }

    private HttpClient CreateClientInternal()
    {
        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new("application/json"));
        return http;
    }

    private async Task<StreamLookup> FetchInternalAsync(HttpClient http, string login)
    {
        try
        {
            // get id based on the username
            using var res = await http.GetAsync($"https://api.picarto.tv/v1/channel/name/{login}");

            if (!res.IsSuccessStatusCode)
            {
                if (res.StatusCode != HttpStatusCode.NotFound)
                    return StreamLookup.Unavailable;

                _failingStreams.TryAdd(login, UtcNow);
                return StreamLookup.NotFound;
            }

            var userData =
                JsonConvert.DeserializeObject<PicartoChannelResponse>(await res.Content.ReadAsStringAsync())!;

            _failingStreams.TryRemove(login, out _);
            return StreamLookup.Found(ToStreamData(userData));
        }
        catch (Exception ex)
        {
            Log.Warning("Something went wrong retreiving {StreamPlatform} stream data for {Login}: {ErrorMessage}",
                Platform,
                login,
                ex.Message);
            return StreamLookup.Unavailable;
        }
    }

    private StreamData ToStreamData(PicartoChannelResponse stream)
        => new()
        {
            StreamType = FollowedStream.FType.Picarto,
            Name = stream.Name,
            UniqueName = stream.Name,
            Viewers = stream.Viewers,
            Title = stream.Title,
            IsLive = stream.Online,
            Preview = stream.Thumbnails.Web,
            Game = stream.Category,
            StreamUrl = $"https://picarto.tv/{stream.Name}",
            AvatarUrl = stream.Avatar
        };
}