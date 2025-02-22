namespace NadekoBot.Modules.Searches.Youtube;

public class YtDlpSearchService : IYoutubeSearchService, INService
{
    private YtdlOperation CreateYtdlOp(int count)
        => new YtdlOperation("-4 "
                             + "--ignore-errors --flat-playlist --skip-download --quiet "
                             + "--geo-bypass "
                             + "--encoding UTF8 "
                             + "--get-id "
                             + "--no-check-certificate "
                             + "--default-search "
                             + $"\"ytsearch{count}:\" -- \"{{0}}\"");

    public async Task<VideoInfo[]?> SearchAsync(string query)
    {
        var op = CreateYtdlOp(5);
        var data = await op.GetDataAsync(query);
        var items = data?.Split('\n');
        if (items is null or { Length: 0 })
            return null;

        return items
            .Map(x => new VideoInfo(x));
    }
}