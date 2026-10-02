using System.Globalization;
using NadekoBot.AiAgent;
using NadekoBot.Modules.Utility.AiAgent;
using NadekoBot.Modules.Utility.Services;

namespace NadekoBot.Modules.Utility.UnitConversion;

public sealed class UnitConversionAiAdapter(ConverterService svc) : IAiToolGroup, INService
{
    public string GroupName => "unit_conversion";

    public string GroupDescription
        => "Convert values between units of measurement and currencies, like km to miles, C to F or USD to EUR.";

    [AiTool("get_unit_conversion",
        "Converts a value from one unit or currency to another. Accepts SI prefixes and combined units like km/h.")]
    public Task<UnitConversionDto> GetUnitConversion(
        [AiParam("The value to convert")] double value,
        [AiParam("Source unit symbol, name or expression, for example km, mph, kWh, °C or USD")] string from,
        [AiParam("Target unit symbol, name or expression, for example mi, km/h, MJ, °F or EUR")] string to)
    {
        var res = svc.Convert(from, to, value);
        return res.Status switch
        {
            ConvertStatus.Ok => Task.FromResult(new UnitConversionDto(
                res.Value,
                ConvertFormatter.FormatQuantity(value, res.From, from, CultureInfo.InvariantCulture),
                ConvertFormatter.FormatQuantity(res.Value, res.To, to, CultureInfo.InvariantCulture))),
            ConvertStatus.UnknownUnit => throw ToolException.NotFound(res.Suggestion is null
                ? $"Unknown unit '{res.FailedToken}'."
                : $"Unknown unit '{res.FailedToken}'. Did you mean '{res.Suggestion}'?"),
            ConvertStatus.InvalidExpression => throw ToolException.InvalidArgument(
                $"Cannot parse unit expression '{res.FailedToken}'."),
            ConvertStatus.DimensionMismatch => throw ToolException.InvalidArgument(
                $"'{from}' and '{to}' measure different quantities."),
            ConvertStatus.RatesUnavailable => throw new ToolException("unavailable",
                "Currency rates are not available yet."),
            _ => throw ToolException.InvalidArgument("The result is too large to represent.")
        };
    }
}

public readonly record struct UnitConversionDto(
    [property: AiParam("Converted numeric value")] double Value,
    [property: AiParam("Source quantity as text")] string From,
    [property: AiParam("Result quantity as text")] string To);
