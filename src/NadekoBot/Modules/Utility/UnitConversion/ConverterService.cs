using System.Text.Json;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Utility.UnitConversion;

namespace NadekoBot.Modules.Utility.Services;

public enum ConvertStatus
{
    Ok,
    UnknownUnit,
    InvalidExpression,
    DimensionMismatch,
    RatesUnavailable,
    NotFinite,
    NoCommonTargets
}

public readonly struct ConvertResult
{
    public ConvertStatus Status { get; init; }
    public ResolvedUnit From { get; init; }
    public ResolvedUnit To { get; init; }
    public double Value { get; init; }
    public string? FailedToken { get; init; }
    public string? Suggestion { get; init; }
}

public readonly record struct CommonConversion(ResolvedUnit Unit, string Symbol, double Value);

public readonly struct CommonConvertResult
{
    public ConvertStatus Status { get; init; }
    public ResolvedUnit From { get; init; }
    public CommonConversion[] Targets { get; init; }
    public string? FailedToken { get; init; }
    public string? Suggestion { get; init; }
}

public sealed class ConverterService(
    IPubSub pubSub,
    IBotCache cache,
    IHttpClientFactory httpFactory,
    ShardData shardData) : INService, IReadyExecutor
{
    private static readonly TypedKey<CurrencyRatesDto> _ratesKey = new("convert:rates:v2");
    private static readonly Dimension _fuelDim = new(length: -2);
    private static readonly Dimension _fuelInverseDim = _fuelDim.Inverse();
    private static readonly TimeSpan _updateInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan _retryInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(30);

    private CurrencyRates _rates = CurrencyRates.Empty;

    public CurrencyRates Rates
        => Volatile.Read(ref _rates);

    public async Task OnReadyAsync()
    {
        await pubSub.Sub(_ratesKey, OnRatesPublishedAsync);

        if ((await cache.GetAsync(_ratesKey)).TryGetValue(out var cached))
            ApplyRates(cached);

        if (shardData.ShardId == 0)
            _ = Task.Run(UpdateLoopInternalAsync);
    }

    private ValueTask OnRatesPublishedAsync(CurrencyRatesDto dto)
    {
        ApplyRates(dto);
        return ValueTask.CompletedTask;
    }

    // Cache reads and pub/sub messages can arrive in any order, so only a newer snapshot replaces the current one.
    public void ApplyRates(CurrencyRatesDto dto)
    {
        var next = CurrencyRates.FromDto(dto);
        if (next.IsEmpty)
            return;

        while (true)
        {
            var cur = Volatile.Read(ref _rates);
            if (!cur.IsEmpty && cur.FetchedAt >= next.FetchedAt)
                return;

            if (ReferenceEquals(Interlocked.CompareExchange(ref _rates, next, cur), cur))
                return;
        }
    }

    private async Task UpdateLoopInternalAsync()
    {
        var current = Rates;
        var age = DateTime.UtcNow - current.FetchedAt;
        if (!current.IsEmpty && age < _updateInterval)
            await Task.Delay(_updateInterval - age);

        while (true)
        {
            var delay = _updateInterval;
            try
            {
                if (!await RefreshRatesAsync())
                {
                    Log.Warning("No currency rate source returned valid rates for .convert");
                    delay = _retryInterval;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unable to update currency rates for .convert");
                delay = _retryInterval;
            }

            await Task.Delay(delay);
        }
    }

    // Keeps the current snapshot when every source fails.
    public async Task<bool> RefreshRatesAsync()
    {
        using var http = httpFactory.CreateClient();
        http.Timeout = _requestTimeout;

        // The ECB snapshot is fetched at most once per refresh, as a check for other sources or as the last resort.
        (CurrencyRatesDto? Dto, CurrencyRates? Rates)? reference = null;

        foreach (var source in RatesSources.All)
        {
            var (dto, parsed) = source.IsReference
                ? reference ??= await TryFetchInternalAsync(http, source)
                : await TryFetchInternalAsync(http, source);

            if (dto is null || parsed is null)
                continue;

            if (!source.IsReference)
            {
                reference ??= await TryFetchInternalAsync(http, RatesSources.Reference);
                if (reference.Value.Rates is { } refRates
                    && !RatesSources.MatchesReference(parsed, refRates, out var code, out var deviation))
                {
                    Log.Warning("Rates from {Source} differ from ECB rates by {Deviation:P1} for {Code}",
                        source.Name, deviation, code);
                    continue;
                }
            }

            dto.FetchedAt = DateTime.UtcNow;
            ApplyRates(dto);
            await cache.AddAsync(_ratesKey, dto);
            await pubSub.Pub(_ratesKey, dto);
            return true;
        }

        return false;
    }

    private static async Task<(CurrencyRatesDto?, CurrencyRates?)> TryFetchInternalAsync(
        HttpClient http,
        RatesSource source)
    {
        try
        {
            await using var stream = await http.GetStreamAsync(source.Url);
            using var doc = await JsonDocument.ParseAsync(stream);
            var dto = source.Parse(doc.RootElement);
            if (dto is null)
            {
                Log.Warning("Currency rate source {Source} returned an unknown format", source.Name);
                return default;
            }

            var rates = CurrencyRates.FromDto(dto);
            if (!RatesSources.HasCoreCodes(rates))
            {
                Log.Warning("Currency rate source {Source} is missing major currencies", source.Name);
                return default;
            }

            return (dto, rates);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Warning("Currency rate source {Source} failed: {Message}", source.Name, ex.Message);
            return default;
        }
    }

    public ConvertResult Convert(ReadOnlySpan<char> from, ReadOnlySpan<char> to, double value)
    {
        var rates = Rates;
        if (!TryResolve(from, rates, out var fromUnit, out var fail)
            || !TryResolve(to, rates, out var toUnit, out fail))
            return fail;

        var status = Compute(fromUnit, toUnit, value, out var result);
        return new()
        {
            Status = status,
            From = fromUnit,
            To = toUnit,
            Value = result
        };
    }

    public CommonConvertResult ConvertToCommon(ReadOnlySpan<char> from, double value)
    {
        var rates = Rates;
        if (!TryResolve(from, rates, out var fromUnit, out var fail))
        {
            return new()
            {
                Status = fail.Status,
                Targets = [],
                FailedToken = fail.FailedToken,
                Suggestion = fail.Suggestion
            };
        }

        var category = fromUnit.Kind == UnitKind.Unit
            ? UnitCatalog.Units[fromUnit.UnitIndex].Category
            : UnitCatalog.TryGetCategory(fromUnit.Dim, out var c)
                ? c
                : (UnitCategory?)null;

        if (category is not { } cat || !UnitCatalog.CommonTargets.TryGetValue(cat, out var targetSymbols))
            return new() { Status = ConvertStatus.NoCommonTargets, From = fromUnit, Targets = [] };

        var buffer = new CommonConversion[targetSymbols.Length];
        var count = 0;
        foreach (var symbol in targetSymbols)
        {
            if (UnitExpressionParser.Parse(symbol, rates, out var target, out _) != ResolveStatus.Ok
                || target.SameAs(fromUnit)
                || Compute(fromUnit, target, value, out var converted) != ConvertStatus.Ok)
                continue;

            buffer[count++] = new(target, symbol, converted);
        }

        return new()
        {
            Status = count == 0 ? ConvertStatus.NoCommonTargets : ConvertStatus.Ok,
            From = fromUnit,
            Targets = count == buffer.Length ? buffer : buffer[..count]
        };
    }

    private static bool TryResolve(
        ReadOnlySpan<char> expr,
        CurrencyRates rates,
        out ResolvedUnit unit,
        out ConvertResult fail)
    {
        expr = expr.Trim();
        var status = UnitExpressionParser.Parse(expr, rates, out unit, out var failedRange);
        if (status == ResolveStatus.Ok)
        {
            fail = default;
            return true;
        }

        var failed = expr[failedRange];
        fail = status switch
        {
            ResolveStatus.Unknown => new()
            {
                Status = ConvertStatus.UnknownUnit,
                FailedToken = failed.ToString(),
                Suggestion = UnitCatalog.Suggest(failed, rates)
            },
            ResolveStatus.RatesUnavailable => new() { Status = ConvertStatus.RatesUnavailable },
            _ => new() { Status = ConvertStatus.InvalidExpression, FailedToken = expr.ToString() }
        };
        return false;
    }

    public static ConvertStatus Compute(in ResolvedUnit from, in ResolvedUnit to, double value, out double result)
    {
        if (from.Dim == to.Dim)
            result = (value * from.Factor + from.Offset - to.Offset) / to.Factor;
        else if (IsFuelEconomyPair(from, to) || IsFuelEconomyPair(to, from))
            result = 1 / (value * from.Factor) / to.Factor;
        else
        {
            result = 0;
            return ConvertStatus.DimensionMismatch;
        }

        return double.IsFinite(result) ? ConvertStatus.Ok : ConvertStatus.NotFinite;
    }

    // Distance per volume and volume per distance convert through the reciprocal. Other inverse pairs,
    // like seconds and hertz, stay a mismatch.
    private static bool IsFuelEconomyPair(in ResolvedUnit distancePerVolume, in ResolvedUnit volumePerDistance)
        => distancePerVolume.Dim == _fuelDim
           && volumePerDistance.Dim == _fuelInverseDim
           && (volumePerDistance.IsRatio
               || (volumePerDistance.Kind == UnitKind.Unit
                   && UnitCatalog.Units[volumePerDistance.UnitIndex].Category == UnitCategory.FuelEconomy));
}
