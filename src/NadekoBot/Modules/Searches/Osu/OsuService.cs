#nullable disable
using NadekoBot.Modules.Searches.Common;
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches;

public sealed class OsuService : INService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IBotCreds _creds;

    public OsuService(IHttpClientFactory httpFactory, IBotCreds creds)
    {
        _httpFactory = httpFactory;
        _creds = creds;
    }

    public async Task<OsuUserData> GetOsuData(string username, string mode)
    {
        using var http = _httpFactory.CreateClient();

        var modeNumber = string.IsNullOrWhiteSpace(mode) ? 0 : ResolveGameMode(mode);
        var userReq = $"https://osu.ppy.sh/api/get_user?k={_creds.OsuApiKey}&u={Uri.EscapeDataString(username)}&m={modeNumber}";
        var userResString = await http.GetStringAsync(userReq);

        if (string.IsNullOrWhiteSpace(userResString))
            return null;
        List<OsuUserData> objs;
        try
        {
            objs = JsonConvert.DeserializeObject<List<OsuUserData>>(userResString);
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("osu!", ex);
            return null;
        }

        if (objs is null || objs.Count == 0)
        {
            return null;
        }

        var obj = objs[0];

        obj.ModeNumber = modeNumber;
        return obj;
    }

    public static int ResolveGameMode(string mode)
    {
        switch (mode?.ToUpperInvariant())
        {
            case "STD":
            case "STANDARD":
                return 0;
            case "TAIKO":
                return 1;
            case "CTB":
            case "CATCHTHEBEAT":
                return 2;
            case "MANIA":
            case "OSU!MANIA":
                return 3;
            default:
                return 0;
        }
    }

    public static string ResolveGameMode(int mode)
    {
        switch (mode)
        {
            case 0:
                return "Standard";
            case 1:
                return "Taiko";
            case 2:
                return "Catch";
            case 3:
                return "Mania";
            default:
                return "Standard";
        }
    }

    public async Task<(GatariUserData userData, GatariUserStats userStats)> GetGatariDataAsync(
        string user,
        string mode)
    {
        using var http = _httpFactory.CreateClient();
        var modeNumber = string.IsNullOrWhiteSpace(mode) ? 0 : ResolveGameMode(mode);

        var resString = await http.GetStringAsync($"https://api.gatari.pw/user/stats?u={Uri.EscapeDataString(user)}&mode={modeNumber}");

        GatariUserStatsResponse statsResponse;
        try
        {
            statsResponse = JsonConvert.DeserializeObject<GatariUserStatsResponse>(resString);
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("Gatari", ex);
            return default;
        }

        if (statsResponse?.Stats is null || statsResponse.Code != 200 || statsResponse.Stats.Id == 0)
        {
            return default;
        }

        var usrResString = await http.GetStringAsync($"https://api.gatari.pw/users/get?u={Uri.EscapeDataString(user)}");

        GatariUserResponse usersResponse;
        try
        {
            usersResponse = JsonConvert.DeserializeObject<GatariUserResponse>(usrResString);
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("Gatari", ex);
            return default;
        }

        if (usersResponse?.Users is not { Count: > 0 } users)
            return default;

        return (users[0], statsResponse.Stats);
    }

    public async Task<(string title, string desc)[]> GetOsuPlay(string user, string mode)
    {
        using var http = _httpFactory.CreateClient();
        var m = 0;
        if (!string.IsNullOrWhiteSpace(mode))
            m = OsuService.ResolveGameMode(mode);

        var reqString = "https://osu.ppy.sh/api/get_user_best"
                        + $"?k={_creds.OsuApiKey}"
                        + $"&u={Uri.EscapeDataString(user)}"
                        + "&type=string"
                        + "&limit=5"
                        + $"&m={m}";

        var resString = await http.GetStringAsync(reqString);
        List<OsuUserBests> obj;
        try
        {
            obj = JsonConvert.DeserializeObject<List<OsuUserBests>>(resString);
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("osu!", ex);
            return [];
        }

        if (obj is null)
            return [];

        var mapTasks = obj.Select(async item =>
        {
            var mapReqString = "https://osu.ppy.sh/api/get_beatmaps"
                               + $"?k={_creds.OsuApiKey}"
                               + $"&b={item.BeatmapId}";

            var mapResString = await http.GetStringAsync(mapReqString);
            OsuMapData map;
            try
            {
                map = JsonConvert.DeserializeObject<List<OsuMapData>>(mapResString).FirstOrDefault();
            }
            catch (JsonException ex)
            {
                SiteAnswer.LogUnreadable("osu!", ex);
                return default;
            }

            if (map is null)
                return default;
            var pp = Math.Round(item.Pp, 2);
            var acc = CalculateAcc(item, m);
            var mods = ResolveMods(item.EnabledMods);

            var title = $"{map.Artist}-{map.Title} ({map.Version})";
            var desc = $@"[/b/{item.BeatmapId}](https://osu.ppy.sh/b/{item.BeatmapId})
        {pp + "pp",-7} | {acc + "%",-7}
        ";
            if (mods != "+")
                desc += Format.Bold(mods);

            return (title, desc);
        });

        // A play without its map has no title, and Discord does not take a field without a name.
        return (await Task.WhenAll(mapTasks)).Where(static p => p.title is not null).ToArray();
    }

    //https://osu.ppy.sh/wiki/Accuracy
    private static double CalculateAcc(OsuUserBests play, int mode)
    {
        double hitPoints;
        double totalHits;
        if (mode == 0)
        {
            hitPoints = (play.Count50 * 50) + (play.Count100 * 100) + (play.Count300 * 300);
            totalHits = play.Count50 + play.Count100 + play.Count300 + play.Countmiss;
            totalHits *= 300;
        }
        else if (mode == 1)
        {
            hitPoints = (play.Countmiss * 0) + (play.Count100 * 0.5) + play.Count300;
            totalHits = (play.Countmiss + play.Count100 + play.Count300) * 300;
            hitPoints *= 300;
        }
        else if (mode == 2)
        {
            hitPoints = play.Count50 + play.Count100 + play.Count300;
            totalHits = play.Countmiss + play.Count50 + play.Count100 + play.Count300 + play.Countkatu;
        }
        else
        {
            hitPoints = (play.Count50 * 50)
                        + (play.Count100 * 100)
                        + (play.Countkatu * 200)
                        + ((play.Count300 + play.Countgeki) * 300);

            totalHits = (play.Countmiss
                         + play.Count50
                         + play.Count100
                         + play.Countkatu
                         + play.Count300
                         + play.Countgeki)
                        * 300;
        }


        return Math.Round(hitPoints / totalHits * 100, 2);
    }


    //https://github.com/ppy/osu-api/wiki#mods
    private static string ResolveMods(int mods)
    {
        var modString = "+";

        if (IsBitSet(mods, 0))
            modString += "NF";
        if (IsBitSet(mods, 1))
            modString += "EZ";
        if (IsBitSet(mods, 8))
            modString += "HT";

        if (IsBitSet(mods, 3))
            modString += "HD";
        if (IsBitSet(mods, 4))
            modString += "HR";
        if (IsBitSet(mods, 6) && !IsBitSet(mods, 9))
            modString += "DT";
        if (IsBitSet(mods, 9))
            modString += "NC";
        if (IsBitSet(mods, 10))
            modString += "FL";

        if (IsBitSet(mods, 5))
            modString += "SD";
        if (IsBitSet(mods, 14))
            modString += "PF";

        if (IsBitSet(mods, 7))
            modString += "RX";
        if (IsBitSet(mods, 11))
            modString += "AT";
        if (IsBitSet(mods, 12))
            modString += "SO";
        return modString;
    }

    private static bool IsBitSet(int mods, int pos)
        => (mods & (1 << pos)) != 0;
}