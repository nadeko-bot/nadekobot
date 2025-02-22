#nullable disable
using NadekoBot.Common.ModuleBehaviors;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Gambling.Services;

public class VoteModel
{
    [JsonPropertyName("userId")]
    public ulong UserId { get; set; }
}

public class VoteRewardService : INService, IReadyExecutor
{
    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;
    private readonly ICurrencyService _currencyService;
    private readonly GamblingConfigService _gamb;

    public VoteRewardService(
        DiscordSocketClient client,
        IBotCreds creds,
        ICurrencyService currencyService,
        GamblingConfigService gamb)
    {
        _client = client;
        _creds = creds;
        _currencyService = currencyService;
        _gamb = gamb;
    }

    public async Task OnReadyAsync()
    {
        if (_client.ShardId != 0)
            return;

        using var http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = delegate { return true; }
        });

        while (true)
        {
            await Task.Delay(30000);

            var topggKey = _creds.Votes?.TopggKey;
            var topggServiceUrl = _creds.Votes?.TopggServiceUrl;

            try
            {
                if (!string.IsNullOrWhiteSpace(topggKey) && !string.IsNullOrWhiteSpace(topggServiceUrl))
                {
                    http.DefaultRequestHeaders.Authorization = new(topggKey);
                    var uri = new Uri(new(topggServiceUrl), "topgg/new");
                    var res = await http.GetStringAsync(uri);
                    var data = JsonSerializer.Deserialize<List<VoteModel>>(res);

                    if (data is { Count: > 0 })
                    {
                        var ids = data.Select(x => x.UserId).ToList();

                        await _currencyService.AddBulkAsync(ids,
                            _gamb.Data.VoteReward,
                            new("vote", "top.gg", "top.gg vote reward"));

                        Log.Information("Rewarding {Count} top.gg voters", ids.Count());
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Critical error loading top.gg vote rewards");
            }

            var discordsKey = _creds.Votes?.DiscordsKey;
            var discordsServiceUrl = _creds.Votes?.DiscordsServiceUrl;

            try
            {
                if (!string.IsNullOrWhiteSpace(discordsKey) && !string.IsNullOrWhiteSpace(discordsServiceUrl))
                {
                    http.DefaultRequestHeaders.Authorization = new(discordsKey);
                    var res = await http.GetStringAsync(new Uri(new(discordsServiceUrl), "discords/new"));
                    var data = JsonSerializer.Deserialize<List<VoteModel>>(res);

                    if (data is { Count: > 0 })
                    {
                        var ids = data.Select(x => x.UserId).ToList();

                        await _currencyService.AddBulkAsync(ids,
                            _gamb.Data.VoteReward,
                            new("vote", "discords", "discords.com vote reward"));

                        Log.Information("Rewarding {Count} discords.com voters", ids.Count());
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Critical error loading discords.com vote rewards");
            }
        }
    }
}