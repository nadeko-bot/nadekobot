#nullable disable
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Utility.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Utility.Services;

public class ConverterService : INService, IReadyExecutor
{
    private static readonly TypedKey<List<ConvertUnit>> _convertKey =
        new("convert:units");

    private readonly TimeSpan _updateInterval = new(12, 0, 0);
    private readonly DiscordSocketClient _client;
    private readonly IBotCache _cache;
    private readonly IHttpClientFactory _httpFactory;

    public ConverterService(
        DiscordSocketClient client,
        IBotCache cache,
        IHttpClientFactory factory)
    {
        _client = client;
        _cache = cache;
        _httpFactory = factory;
    }

    public async Task OnReadyAsync()
    {
        if (_client.ShardId != 0)
            return;

        using var timer = new PeriodicTimer(_updateInterval);
        do
        {
            try
            {
                await UpdateCurrency();
            }
            catch
            {
                // ignored
            }
        } while (await timer.WaitForNextTickAsync());
    }

    private async Task<Rates> GetCurrencyRates()
    {
        using var http = _httpFactory.CreateClient();
        var res = await http.GetStringAsync("https://convertapi.nadeko.bot/latest");
        return JsonSerializer.Deserialize<Rates>(res);
    }

    private async Task UpdateCurrency()
    {
        var unitTypeString = "currency";
        var currencyRates = await GetCurrencyRates();
        var baseType = new ConvertUnit
        {
            Triggers = [currencyRates.Base],
            Modifier = decimal.One,
            UnitType = unitTypeString
        };
        var units = currencyRates.ConversionRates.Select(u => new ConvertUnit
                                 {
                                     Triggers = [u.Key],
                                     Modifier = u.Value,
                                     UnitType = unitTypeString
                                 })
                                 .ToList();

        var stream =  File.OpenRead("data/units.json");
        var defaultUnits = await JsonSerializer.DeserializeAsync<ConvertUnit[]>(stream);
        if(defaultUnits is not null)
            units.AddRange(defaultUnits);
        
        units.Add(baseType);
        
        await _cache.AddAsync(_convertKey, units);
    }

    public async Task<IReadOnlyList<ConvertUnit>> GetUnitsAsync()
        => (await _cache.GetAsync(_convertKey)).TryGetValue(out var list)
            ? list
            : Array.Empty<ConvertUnit>();
}

public class Rates
{
    [JsonPropertyName("base")]
    public string Base { get; set; }
    
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("rates")]
    public Dictionary<string, decimal> ConversionRates { get; set; }
}