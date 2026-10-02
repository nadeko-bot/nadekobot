using System.Globalization;
using System.Text.Json;

namespace NadekoBot.Modules.Utility.UnitConversion;

public sealed record RatesSource(string Name, string Url, bool IsReference, Func<JsonElement, CurrencyRatesDto?> Parse);

public static class RatesSources
{
    public const double MAX_REFERENCE_DEVIATION = 0.05;

    private const string FEED_BASE = "eur";

    private static readonly string[] _coreCodes = ["USD", "GBP", "JPY", "CHF", "CAD"];

    public static readonly RatesSource Reference = new(
        "frankfurter",
        "https://api.frankfurter.dev/v1/latest",
        true,
        ParseFrankfurter);

    // Tried in order. The community feed has the most currencies, and its README asks for the pages.dev mirror
    // as a fallback. The ECB data from Frankfurter is the last resort.
    public static readonly RatesSource[] All =
    [
        new("currency-api", "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies/eur.min.json",
            false, ParseFeed),
        new("currency-api-mirror", "https://latest.currency-api.pages.dev/v1/currencies/eur.min.json",
            false, ParseFeed),
        Reference
    ];

    public static CurrencyRatesDto? ParseFeed(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(FEED_BASE, out var rates)
            || !TryGetDate(root, out var date))
            return null;

        return Build(FEED_BASE, date, rates);
    }

    public static CurrencyRatesDto? ParseFrankfurter(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("base", out var baseEl)
            || baseEl.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("rates", out var rates)
            || !TryGetDate(root, out var date))
            return null;

        return Build(baseEl.GetString()!, date, rates);
    }

    private static bool TryGetDate(JsonElement root, out DateTime date)
    {
        date = default;
        return root.TryGetProperty("date", out var el)
               && el.ValueKind == JsonValueKind.String
               && DateTime.TryParseExact(el.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date);
    }

    private static CurrencyRatesDto? Build(string baseCode, DateTime date, JsonElement rates)
    {
        if (rates.ValueKind != JsonValueKind.Object || !CurrencyCodes.IsSupported(baseCode))
            return null;

        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in rates.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number
                && CurrencyCodes.IsSupported(prop.Name)
                && prop.Value.TryGetDouble(out var rate)
                && double.IsFinite(rate)
                && rate > 0)
                dict[prop.Name.ToUpperInvariant()] = rate;
        }

        return new()
        {
            Base = baseCode.ToUpperInvariant(),
            Date = date,
            Rates = dict
        };
    }

    public static bool HasCoreCodes(CurrencyRates rates)
    {
        foreach (var code in _coreCodes)
        {
            if (!rates.TryGet(code, out _))
                return false;
        }

        return true;
    }

    // Compares cross rates against USD, so the two snapshots may use different base currencies.
    public static bool MatchesReference(
        CurrencyRates candidate,
        CurrencyRates reference,
        out string? worstCode,
        out double worstDeviation)
    {
        worstCode = null;
        worstDeviation = 0;
        if (!candidate.TryGet("USD", out var candUsd) || !reference.TryGet("USD", out var refUsd))
            return true;

        foreach (var code in reference.Codes)
        {
            if (!candidate.TryGet(code, out var cand) || !reference.TryGet(code, out var refInfo))
                continue;

            var deviation = Math.Abs(cand.Factor / candUsd.Factor / (refInfo.Factor / refUsd.Factor) - 1);
            if (deviation > worstDeviation)
            {
                worstDeviation = deviation;
                worstCode = code;
            }
        }

        return worstDeviation <= MAX_REFERENCE_DEVIATION;
    }
}
