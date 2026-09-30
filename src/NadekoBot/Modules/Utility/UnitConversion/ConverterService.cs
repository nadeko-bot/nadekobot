using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Utility.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Utility.Services;

public enum ConvertStatus
{
    Ok,
    NotFound,
    TypeMismatch,
    Overflow
}

public readonly record struct ConvertResult(
    ConvertStatus Status,
    ConvertUnit? From = null,
    ConvertUnit? To = null,
    decimal Value = 0);

public sealed class ConverterService(
    DiscordSocketClient client,
    IBotCache cache,
    IHttpClientFactory httpFactory) : INService, IReadyExecutor
{
    public const string UNITS_PATH = "data/units.json";
    private const string CURRENCY_TYPE = "currency";
    private const string TEMPERATURE_TYPE = "temperature";
    private const int RESULT_DECIMALS = 4;

    private static readonly TypedKey<List<ConvertUnit>> _currencyKey = new("convert:currency");

    private static readonly TimeSpan _updateInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan _retryInterval = TimeSpan.FromMinutes(5);

    // loaded on every shard, so the fixed units never depend on the currency rate service
    private volatile ConvertUnit[] _staticUnits = [];

    public async Task OnReadyAsync()
    {
        await LoadStaticUnitsAsync();

        if (client.ShardId == 0)
            _ = Task.Run(UpdateLoopInternalAsync);
    }

    public async Task LoadStaticUnitsAsync()
    {
        try
        {
            await using var stream = File.OpenRead(UNITS_PATH);
            _staticUnits = await JsonSerializer.DeserializeAsync<ConvertUnit[]>(stream) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unable to load {UnitsPath}", UNITS_PATH);
        }
    }

    private async Task UpdateLoopInternalAsync()
    {
        while (true)
        {
            var delay = _updateInterval;
            try
            {
                await UpdateCurrencyInternalAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unable to update currency rates for .convert");
                delay = _retryInterval;
            }

            await Task.Delay(delay);
        }
    }

    private async Task UpdateCurrencyInternalAsync()
    {
        using var http = httpFactory.CreateClient();
        var res = await http.GetStringAsync("https://convertapi.nadeko.bot/latest");
        var rates = JsonSerializer.Deserialize<Rates>(res);
        if (rates?.Base is null || rates.ConversionRates is null)
            return;

        var units = new List<ConvertUnit>(rates.ConversionRates.Count + 1)
        {
            new()
            {
                Triggers = [rates.Base],
                Modifier = decimal.One,
                UnitType = CURRENCY_TYPE
            }
        };

        foreach (var (code, rate) in rates.ConversionRates)
        {
            if (rate <= 0 || string.Equals(code, rates.Base, StringComparison.OrdinalIgnoreCase))
                continue;

            units.Add(new()
            {
                Triggers = [code],
                Modifier = rate,
                UnitType = CURRENCY_TYPE
            });
        }

        await cache.AddAsync(_currencyKey, units);
    }

    private async Task<IReadOnlyList<ConvertUnit>> GetCurrencyUnitsAsync()
        => (await cache.GetAsync(_currencyKey)).TryGetValue(out var list)
            ? list
            : [];

    public async Task<IReadOnlyList<ConvertUnit>> GetUnitsAsync()
    {
        var currency = await GetCurrencyUnitsAsync();
        var staticUnits = _staticUnits;
        var all = new List<ConvertUnit>(staticUnits.Length + currency.Count);
        all.AddRange(staticUnits);
        all.AddRange(currency);
        return all;
    }

    private static ConvertUnit? Find(IReadOnlyList<ConvertUnit> units, string trigger)
    {
        foreach (var unit in units)
        {
            foreach (var t in unit.Triggers)
            {
                if (string.Equals(t, trigger, StringComparison.InvariantCultureIgnoreCase))
                    return unit;
            }
        }

        return null;
    }

    public async Task<ConvertResult> ConvertAsync(string origin, string target, decimal value)
    {
        var staticUnits = _staticUnits;
        var from = Find(staticUnits, origin);
        var to = Find(staticUnits, target);

        if (from is null || to is null)
        {
            var currency = await GetCurrencyUnitsAsync();
            from ??= Find(currency, origin);
            to ??= Find(currency, target);
        }

        if (from is null || to is null)
            return new(ConvertStatus.NotFound);

        if (!string.Equals(from.UnitType, to.UnitType, StringComparison.Ordinal))
            return new(ConvertStatus.TypeMismatch, from, to);

        try
        {
            var res = Math.Round(Compute(from, to, value), RESULT_DECIMALS);
            return new(ConvertStatus.Ok, from, to, res);
        }
        catch (OverflowException)
        {
            return new(ConvertStatus.Overflow, from, to);
        }
    }

    private static decimal Compute(ConvertUnit from, ConvertUnit to, decimal value)
    {
        if (ReferenceEquals(from, to))
            return value;

        if (from.UnitType == TEMPERATURE_TYPE)
        {
            // convert to kelvin first, then to the target
            var kelvin = from.Triggers[0] switch
            {
                "C" => value + 273.15m,
                "F" => (value + 459.67m) * (5m / 9m),
                _ => value
            };

            return to.Triggers[0] switch
            {
                "C" => kelvin - 273.15m,
                "F" => kelvin * (9m / 5m) - 459.67m,
                _ => kelvin
            };
        }

        return from.UnitType == CURRENCY_TYPE
            ? value * to.Modifier / from.Modifier
            : value * from.Modifier / to.Modifier;
    }
}

public class Rates
{
    [JsonPropertyName("base")]
    public string? Base { get; set; }

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("rates")]
    public Dictionary<string, decimal>? ConversionRates { get; set; }
}
