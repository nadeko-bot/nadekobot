using System.Buffers.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class NasdaqEnvelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("status")]
    public NasdaqStatus? Status { get; set; }
}

public sealed class NasdaqStatus
{
    // 400 when the symbol does not exist.
    [JsonPropertyName("rCode")]
    public int Code { get; set; }
}

public sealed class NasdaqIntradayChart
{
    [JsonPropertyName("company")]
    public string? Company { get; set; }

    [JsonPropertyName("exchange")]
    public string? Exchange { get; set; }

    // "Nov 13, 2026 11:49 AM ET"
    [JsonPropertyName("timeAsOf")]
    public string? TimeAsOf { get; set; }

    [JsonPropertyName("lastSalePrice")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double LastSalePrice { get; set; } = double.NaN;

    [JsonPropertyName("previousClose")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double PreviousClose { get; set; } = double.NaN;

    [JsonPropertyName("percentageChange")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double PercentageChange { get; set; } = double.NaN;

    [JsonPropertyName("volume")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double Volume { get; set; } = double.NaN;

    [JsonPropertyName("chart")]
    public NasdaqIntradayPoint[]? Chart { get; set; }
}

// X is the time of the minute in milliseconds, with the New York wall clock written as if it were UTC.
public struct NasdaqIntradayPoint
{
    [JsonPropertyName("x")]
    public long X { get; set; }

    [JsonPropertyName("y")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double Y { get; set; }
}

// The daily prices, the newest day first.
public sealed class NasdaqHistory
{
    [JsonPropertyName("tradesTable")]
    public NasdaqTradesTable? TradesTable { get; set; }
}

public sealed class NasdaqTradesTable
{
    [JsonPropertyName("rows")]
    public NasdaqHistoryRow[]? Rows { get; set; }
}

public struct NasdaqHistoryRow
{
    [JsonPropertyName("date")]
    [JsonConverter(typeof(NasdaqDateConverter))]
    public DateOnly Date { get; set; }

    [JsonPropertyName("open")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double Open { get; set; }

    [JsonPropertyName("high")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double High { get; set; }

    [JsonPropertyName("low")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double Low { get; set; }

    [JsonPropertyName("close")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double Close { get; set; }

    [JsonPropertyName("volume")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double Volume { get; set; }
}

public sealed class NasdaqInfo
{
    [JsonPropertyName("companyName")]
    public string? CompanyName { get; set; }

    [JsonPropertyName("exchange")]
    public string? Exchange { get; set; }

    [JsonPropertyName("primaryData")]
    public NasdaqPrimaryData? PrimaryData { get; set; }
}

public sealed class NasdaqPrimaryData
{
    [JsonPropertyName("lastSalePrice")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double LastSalePrice { get; set; } = double.NaN;

    [JsonPropertyName("netChange")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double NetChange { get; set; } = double.NaN;

    [JsonPropertyName("percentageChange")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double PercentageChange { get; set; } = double.NaN;

    [JsonPropertyName("volume")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double Volume { get; set; } = double.NaN;

    // "Nov 13, 2026 12:59 PM ET"
    [JsonPropertyName("lastTradeTimestamp")]
    public string? LastTradeTimestamp { get; set; }
}

public sealed class NasdaqSummary
{
    [JsonPropertyName("summaryData")]
    public NasdaqSummaryData? SummaryData { get; set; }
}

public sealed class NasdaqSummaryData
{
    [JsonPropertyName("Exchange")]
    public NasdaqTextField Exchange { get; set; }

    [JsonPropertyName("MarketCap")]
    public NasdaqNumberField MarketCap { get; set; }

    [JsonPropertyName("TodayHighLow")]
    public NasdaqRangeField TodayHighLow { get; set; }

    // Nasdaq writes the key with this typo.
    [JsonPropertyName("FiftTwoWeekHighLow")]
    public NasdaqRangeField FiftTwoWeekHighLow { get; set; }

    [JsonPropertyName("FiftyTwoWeekHighLow")]
    public NasdaqRangeField FiftyTwoWeekHighLow { get; set; }
}

public struct NasdaqTextField
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

public struct NasdaqNumberField
{
    [JsonPropertyName("value")]
    [JsonConverter(typeof(NasdaqNumberConverter))]
    public double Value { get; set; }
}

public struct NasdaqRangeField
{
    [JsonPropertyName("value")]
    [JsonConverter(typeof(NasdaqRangeConverter))]
    public PriceRange Value { get; set; }
}

public sealed class NasdaqSearchItem
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // STOCKS, ETF, MUTUALFUNDS, ...
    [JsonPropertyName("asset")]
    public string? Asset { get; set; }
}

public sealed class CmcHistoryEnvelope
{
    [JsonPropertyName("data")]
    public CmcHistoryData? Data { get; set; }
}

public sealed class CmcHistoryData
{
    [JsonPropertyName("quotes")]
    public CmcHistoryItem[]? Quotes { get; set; }
}

public struct CmcHistoryItem
{
    [JsonPropertyName("timeOpen")]
    public DateTimeOffset TimeOpen { get; set; }

    [JsonPropertyName("quote")]
    public CmcOhlcv Quote { get; set; }
}

// Volume is the volume of the 24 hours before the end of the interval.
public struct CmcOhlcv
{
    [JsonPropertyName("open")]
    public double Open { get; set; }

    [JsonPropertyName("high")]
    public double High { get; set; }

    [JsonPropertyName("low")]
    public double Low { get; set; }

    [JsonPropertyName("close")]
    public double Close { get; set; }

    [JsonPropertyName("volume")]
    public double Volume { get; set; }
}

public readonly record struct PriceRange(double Low, double High)
{
    public PriceRange? OrNull()
        => Low > 0 && High >= Low && double.IsFinite(High) ? this : null;
}

// Reads a JSON number or text such as "$1,234.56", "+0.73%" or "N/A". Text which is not a number gives NaN.
public sealed class NasdaqNumberConverter : JsonConverter<double>
{
    public const int MAX_LENGTH = 64;

    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetDouble(out var number) ? number : double.NaN;
            case JsonTokenType.String:
                Span<byte> text = stackalloc byte[MAX_LENGTH];
                return NasdaqText.TryCopy(ref reader, text, out var length) ? NasdaqText.ParseNumber(text[..length]) : double.NaN;
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                reader.Skip();
                return double.NaN;
            default:
                return double.NaN;
        }
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsFinite(value))
            writer.WriteNumberValue(value);
        else
            writer.WriteNullValue();
    }
}

// Reads "$170/$158.39", the high and the low of a range.
public sealed class NasdaqRangeConverter : JsonConverter<PriceRange>
{
    public override PriceRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
            return default;
        }

        Span<byte> text = stackalloc byte[NasdaqNumberConverter.MAX_LENGTH];
        if (reader.TokenType != JsonTokenType.String || !NasdaqText.TryCopy(ref reader, text, out var length))
            return default;

        var value = text[..length];
        var slash = value.IndexOf((byte)'/');
        if (slash < 0)
            return default;

        var first = NasdaqText.ParseNumber(value[..slash]);
        var second = NasdaqText.ParseNumber(value[(slash + 1)..]);
        if (!(first > 0 && second > 0))
            return default;

        return new(Math.Min(first, second), Math.Max(first, second));
    }

    public override void Write(Utf8JsonWriter writer, PriceRange value, JsonSerializerOptions options)
        => writer.WriteStringValue(string.Create(CultureInfo.InvariantCulture, $"{value.High}/{value.Low}"));
}

// Reads "11/12/2026" (month, day, year). Other text gives default.
public sealed class NasdaqDateConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
            return default;
        }

        Span<byte> text = stackalloc byte[NasdaqNumberConverter.MAX_LENGTH];
        if (reader.TokenType != JsonTokenType.String || !NasdaqText.TryCopy(ref reader, text, out var length))
            return default;

        var value = text[..length];
        if (!TryReadPart(ref value, out var month)
            || !TryReadPart(ref value, out var day)
            || !Utf8Parser.TryParse(value, out int year, out var consumed)
            || consumed != value.Length
            || month is < 1 or > 12
            || year is < 1 or > 9999
            || day < 1
            || day > DateTime.DaysInMonth(year, month))
            return default;

        return new(year, month, day);
    }

    private static bool TryReadPart(ref Span<byte> text, out int part)
    {
        if (!Utf8Parser.TryParse(text, out part, out var consumed)
            || consumed >= text.Length
            || text[consumed] != (byte)'/')
            return false;

        text = text[(consumed + 1)..];
        return true;
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture));
}

public static class NasdaqText
{
    public static bool TryCopy(ref Utf8JsonReader reader, scoped Span<byte> buffer, out int length)
    {
        length = 0;
        var raw = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
        if (raw > buffer.Length)
            return false;

        length = reader.CopyString(buffer);
        return true;
    }

    public static double ParseNumber(ReadOnlySpan<byte> text)
    {
        Span<byte> digits = stackalloc byte[NasdaqNumberConverter.MAX_LENGTH];
        var count = 0;
        foreach (var b in text)
        {
            if (b is (byte)'$' or (byte)',' or (byte)'%' or (byte)'+' or (byte)' ')
                continue;

            if (count == digits.Length)
                return double.NaN;

            digits[count++] = b;
        }

        return count > 0
               && Utf8Parser.TryParse(digits[..count], out double value, out var consumed)
               && consumed == count
               && double.IsFinite(value)
            ? value
            : double.NaN;
    }
}

[JsonSerializable(typeof(NasdaqEnvelope<NasdaqIntradayChart>))]
[JsonSerializable(typeof(NasdaqEnvelope<NasdaqHistory>))]
[JsonSerializable(typeof(NasdaqEnvelope<NasdaqInfo>))]
[JsonSerializable(typeof(NasdaqEnvelope<NasdaqSummary>))]
[JsonSerializable(typeof(NasdaqEnvelope<NasdaqSearchItem[]>))]
[JsonSerializable(typeof(CmcHistoryEnvelope))]
public sealed partial class FinanceJsonContext : JsonSerializerContext
{
}
