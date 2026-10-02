using System.Collections.Frozen;

namespace NadekoBot.Modules.Utility.UnitConversion;

public static class CurrencyCodes
{
    // Active ISO 4217 currencies plus precious metals. Rate feeds also carry crypto tokens and withdrawn
    // currencies, which the bot ignores.
    private static readonly FrozenSet<string> _supported = FrozenSet.ToFrozenSet(
    [
        "AED", "AFN", "ALL", "AMD", "AOA", "ARS", "AUD", "AWG", "AZN", "BAM", "BBD", "BDT", "BGN", "BHD", "BIF",
        "BMD", "BND", "BOB", "BRL", "BSD", "BTN", "BWP", "BYN", "BZD", "CAD", "CDF", "CHF", "CLP", "CNY", "COP",
        "CRC", "CUP", "CVE", "CZK", "DJF", "DKK", "DOP", "DZD", "EGP", "ERN", "ETB", "EUR", "FJD", "FKP", "GBP",
        "GEL", "GHS", "GIP", "GMD", "GNF", "GTQ", "GYD", "HKD", "HNL", "HTG", "HUF", "IDR", "ILS", "INR", "IQD",
        "IRR", "ISK", "JMD", "JOD", "JPY", "KES", "KGS", "KHR", "KMF", "KPW", "KRW", "KWD", "KYD", "KZT", "LAK",
        "LBP", "LKR", "LRD", "LSL", "LYD", "MAD", "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR",
        "MWK", "MXN", "MYR", "MZN", "NAD", "NGN", "NIO", "NOK", "NPR", "NZD", "OMR", "PAB", "PEN", "PGK", "PHP",
        "PKR", "PLN", "PYG", "QAR", "RON", "RSD", "RUB", "RWF", "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP",
        "SLE", "SOS", "SRD", "SSP", "STN", "SVC", "SYP", "SZL", "THB", "TJS", "TMT", "TND", "TOP", "TRY", "TTD",
        "TWD", "TZS", "UAH", "UGX", "USD", "UYU", "UZS", "VES", "VND", "VUV", "WST", "XAF", "XCD", "XCG", "XOF",
        "XPF", "YER", "ZAR", "ZMW", "ZWG",
        "XAU", "XAG", "XPT", "XPD"
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup
        = _supported.GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly FrozenDictionary<string, string> _metalNames = new Dictionary<string, string>
    {
        ["XAU"] = "Gold (troy ounce)",
        ["XAG"] = "Silver (troy ounce)",
        ["XPT"] = "Platinum (troy ounce)",
        ["XPD"] = "Palladium (troy ounce)"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _metalLookup
        = _metalNames.GetAlternateLookup<ReadOnlySpan<char>>();

    public static bool IsSupported(ReadOnlySpan<char> code)
        => _lookup.Contains(code);

    public static string? GetMetalName(ReadOnlySpan<char> code)
        => _metalLookup.TryGetValue(code, out var name) ? name : null;
}
