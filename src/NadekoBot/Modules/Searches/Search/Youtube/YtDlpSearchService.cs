namespace NadekoBot.Modules.Searches.Youtube;

public class YtDlpSearchService(BotPolicy policy, PublicOnlyProxy proxy) : IYoutubeSearchService, INService
{
    private const int RESULT_COUNT = 5;

    private readonly YtdlOperation _op = new(
        [
            "-4",
            "--ignore-errors", "--flat-playlist", "--skip-download", "--quiet",
            "--geo-bypass",
            "--encoding", "UTF8",
            "--get-id",
            "--no-check-certificate",
            "--default-search", $"ytsearch{RESULT_COUNT}:", "--"
        ], policy, proxy);

    public async Task<VideoInfo[]?> SearchAsync(string query)
    {
        var data = await _op.SearchAsync(query, RESULT_COUNT);
        var items = data?.Split('\n');
        if (items is null or { Length: 0 })
            return null;

        return items
            .Map(x => new VideoInfo(x));
    }
}