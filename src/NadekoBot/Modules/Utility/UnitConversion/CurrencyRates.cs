using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;

namespace NadekoBot.Modules.Utility.UnitConversion;

public sealed class CurrencyRatesDto
{
    public string Base { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime FetchedAt { get; set; }
    public Dictionary<string, double> Rates { get; set; } = [];
}

public readonly record struct CurrencyInfo(string Code, double Factor);

public sealed class CurrencyRates
{
    public const int CODE_LENGTH = 3;

    private static readonly SearchValues<char> _letters
        = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    public static readonly CurrencyRates Empty = new([], DateTime.MinValue, DateTime.MinValue);

    private static readonly Lazy<FrozenDictionary<string, string>> _names = new(BuildNames);

    private readonly FrozenDictionary<string, CurrencyInfo> _byCode;
    private readonly FrozenDictionary<string, CurrencyInfo>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    public DateTime Date { get; }
    public DateTime FetchedAt { get; }
    public string[] Codes { get; }
    public bool IsEmpty => Codes.Length == 0;

    private CurrencyRates(Dictionary<string, CurrencyInfo> byCode, DateTime date, DateTime fetchedAt)
    {
        // codes are validated as ASCII letters, so ordinal ignore-case is equivalent to invariant
        _byCode = byCode.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _lookup = _byCode.GetAlternateLookup<ReadOnlySpan<char>>();
        Date = date;
        FetchedAt = fetchedAt;

        var codes = new string[byCode.Count];
        var i = 0;
        foreach (var kv in byCode)
            codes[i++] = kv.Value.Code;
        Array.Sort(codes, StringComparer.Ordinal);
        Codes = codes;
    }

    // The source gives "1 base = rate X", so one X is 1/rate base units.
    public static CurrencyRates FromDto(CurrencyRatesDto dto)
    {
        if (!IsCode(dto.Base) || !CurrencyCodes.IsSupported(dto.Base))
            return Empty;

        var map = new Dictionary<string, CurrencyInfo>(dto.Rates.Count + 1, StringComparer.OrdinalIgnoreCase);
        var baseCode = dto.Base.ToUpperInvariant();
        map[baseCode] = new(baseCode, 1);

        foreach (var (code, rate) in dto.Rates)
        {
            if (!IsCode(code) || !CurrencyCodes.IsSupported(code) || !double.IsFinite(rate) || rate <= 0)
                continue;

            var upper = code.ToUpperInvariant();
            map.TryAdd(upper, new(upper, 1 / rate));
        }

        return new(map, dto.Date, dto.FetchedAt);
    }

    public static bool IsCode(ReadOnlySpan<char> code)
        => code.Length == CODE_LENGTH && !code.ContainsAnyExcept(_letters);

    public static bool IsUpperCode(ReadOnlySpan<char> code)
        => code.Length == CODE_LENGTH && !code.ContainsAnyExceptInRange('A', 'Z');

    public bool TryGet(ReadOnlySpan<char> code, out CurrencyInfo info)
        => _lookup.TryGetValue(code, out info);

    public static string? GetName(ReadOnlySpan<char> code)
        => CurrencyCodes.GetMetalName(code)
           ?? (_names.Value.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(code, out var name) ? name : null);

    public static bool IsKnownCode(ReadOnlySpan<char> code)
        => IsCode(code) && CurrencyCodes.IsSupported(code);

    private static FrozenDictionary<string, string> BuildNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                try
                {
                    var region = new RegionInfo(culture.Name);
                    if (IsCode(region.ISOCurrencySymbol))
                        names.TryAdd(region.ISOCurrencySymbol, region.CurrencyEnglishName);
                }
                catch (ArgumentException)
                {
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to load currency names");
        }

        return names.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
