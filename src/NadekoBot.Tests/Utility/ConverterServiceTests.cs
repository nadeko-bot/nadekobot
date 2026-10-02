using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Modules.Utility.UnitConversion;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class ConverterServiceTests
{
    private const double TOLERANCE = 1e-9;

    private const string FEED = "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies/eur.min.json";
    private const string MIRROR = "https://latest.currency-api.pages.dev/v1/currencies/eur.min.json";
    private const string ECB = "https://api.frankfurter.dev/v1/latest";

    private ConverterService _svc = null!;
    private FakeHandler _http = null!;

    [SetUp]
    public void SetUp()
    {
        _http = new FakeHandler();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_http, false));
        _svc = new ConverterService(
            new EventPubSub(),
            new MemoryBotCache(),
            factory,
            new ShardData(Substitute.For<DiscordSocketClient>(), Substitute.For<IBotCreds>()));
    }

    [TearDown]
    public void TearDown()
        => _http.Dispose();

    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly Dictionary<string, string> Responses = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Responses.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private static string Feed(double usd)
        => $$$"""{"date":"2026-10-01","eur":{"eur":1,"usd":{{{usd}}},"gbp":0.85,"jpy":160,"chf":0.94,"cad":1.6,"ngn":1500,"xau":0.00027,"btc":0.0000134,"dem":1.95583}}""";

    private const string ECB_BODY
        = """{"amount":1.0,"base":"EUR","date":"2026-10-01","rates":{"USD":1.17,"GBP":0.85,"JPY":160,"CHF":0.94,"CAD":1.6}}""";

    private void ApplyEuroRates(DateTime fetchedAt, double usd = 1.25)
        => _svc.ApplyRates(new CurrencyRatesDto
        {
            Base = "EUR",
            Date = fetchedAt.Date,
            FetchedAt = fetchedAt,
            Rates = new Dictionary<string, double>
            {
                ["USD"] = usd,
                ["GBP"] = 0.85,
                ["JPY"] = 160,
                ["CNY"] = 8,
                ["INR"] = 95,
                ["CAD"] = 1.5,
                ["AUD"] = 1.6,
                ["CUP"] = 25
            }
        });

    private double Ok(string from, string to, double value)
    {
        var res = _svc.Convert(from, to, value);
        Assert.That(res.Status, Is.EqualTo(ConvertStatus.Ok), $"{value} {from} -> {to}");
        return res.Value;
    }

    private static void AssertClose(double actual, double expected)
        => Assert.That(actual, Is.EqualTo(expected).Within(Math.Abs(expected) * TOLERANCE));

    [Test]
    public void SimpleAndPrefixedUnits()
    {
        AssertClose(Ok("m", "km", 1500), 1.5);
        AssertClose(Ok("MiB", "MB", 1), 1.048576);
        AssertClose(Ok("Mm", "mm", 1), 1e9);
        AssertClose(Ok("KM", "m", 2), 2000);
        AssertClose(Ok("kilometers", "miles", 1.609344), 1);
        AssertClose(Ok("mb", "Pa", 5), 500);
        AssertClose(Ok("lb", "kg", 1), 0.45359237);

        Assert.That(_svc.Convert("MG", "g", 1).Status, Is.EqualTo(ConvertStatus.UnknownUnit));
        Assert.That(_svc.Convert("dB", "B", 1).Status, Is.EqualTo(ConvertStatus.UnknownUnit));
    }

    [Test]
    public void TemperatureUsesOffsets()
    {
        AssertClose(Ok("C", "F", 100), 212);
        AssertClose(Ok("°F", "K", 32), 273.15);
        AssertClose(Ok("K", "celsius", 0), -273.15);
        AssertClose(Ok("°R", "°F", 491.67), 32);
    }

    [Test]
    public void CompoundUnitsAndFuelEconomy()
    {
        AssertClose(Ok("km/h", "mph", 100), 100 / 1.609344);
        AssertClose(Ok("m/s", "km/h", 10), 36);
        AssertClose(Ok("kWh", "MJ", 1), 3.6);
        AssertClose(Ok("m2", "ha", 20000), 2);
        AssertClose(Ok("ft^3", "L", 1), 28.316846592);
        AssertClose(Ok("kg*m/s^2", "N", 3), 3);
        AssertClose(Ok("mpg", "L/100km", 30), 100 * 3.785411784 / (30 * 1.609344));
        AssertClose(Ok("L/100km", "km/L", 5), 20);

        Assert.That(_svc.Convert("s", "Hz", 2).Status, Is.EqualTo(ConvertStatus.DimensionMismatch));
        Assert.That(_svc.Convert("mpg", "m2", 2).Status, Is.EqualTo(ConvertStatus.DimensionMismatch));
        Assert.That(_svc.Convert("m^20", "m", 1).Status, Is.EqualTo(ConvertStatus.InvalidExpression));
    }

    [Test]
    public void MismatchAndUnknownUnits()
    {
        Assert.That(_svc.Convert("m", "kg", 1).Status, Is.EqualTo(ConvertStatus.DimensionMismatch));

        var unknown = _svc.Convert("feeet", "m", 1);
        Assert.That(unknown.Status, Is.EqualTo(ConvertStatus.UnknownUnit));
        Assert.That(unknown.FailedToken, Is.EqualTo("feeet"));
        Assert.That(unknown.Suggestion, Is.EqualTo("feet"));

        var inCompound = _svc.Convert("km/hourz", "mph", 1);
        Assert.That(inCompound.Status, Is.EqualTo(ConvertStatus.UnknownUnit));
        Assert.That(inCompound.FailedToken, Is.EqualTo("hourz"));
    }

    [Test]
    public void CurrencyUsesNewestSnapshot()
    {
        Assert.That(_svc.Convert("usd", "eur", 1).Status, Is.EqualTo(ConvertStatus.RatesUnavailable));
        AssertClose(Ok("cup", "mL", 1), 236.5882365);

        var now = DateTime.UtcNow;
        ApplyEuroRates(now);
        ApplyEuroRates(now.AddHours(-1), usd: 2);

        AssertClose(Ok("eur", "usd", 10), 12.5);
        AssertClose(Ok("USD/gal", "EUR/L", 1), 1 / 1.25 / 3.785411784);
        AssertClose(Ok("CUP", "EUR", 25), 1);
        AssertClose(Ok("cup", "mL", 1), 236.5882365);

        ApplyEuroRates(now.AddHours(1), usd: 2);
        AssertClose(Ok("EUR", "USD", 1), 2);
    }

    [Test]
    public void CommonTargetsExcludeSourceAndResolve()
    {
        ApplyEuroRates(DateTime.UtcNow);

        var res = _svc.ConvertToCommon("km", 5);
        Assert.That(res.Status, Is.EqualTo(ConvertStatus.Ok));
        Assert.That(res.Targets.Select(x => x.Symbol), Does.Contain("mi").And.Not.Contain("km"));

        foreach (var (category, symbols) in UnitCatalog.CommonTargets)
        {
            foreach (var symbol in symbols)
            {
                Assert.That(UnitExpressionParser.Parse(symbol, _svc.Rates, out var unit, out _),
                    Is.EqualTo(ResolveStatus.Ok), symbol);

                var actual = unit.Kind == UnitKind.Unit
                    ? UnitCatalog.Units[unit.UnitIndex].Category
                    : UnitCatalog.TryGetCategory(unit.Dim, out var c) ? c : (UnitCategory?)null;
                Assert.That(actual, Is.EqualTo(category), symbol);
            }
        }
    }

    [Test]
    public void InputForms()
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        var de = CultureInfo.GetCultureInfo("de-DE");

        void Check(string input, CultureInfo culture, double value, string from, string to)
        {
            Assert.That(ConvertInputParser.TryParse(input, culture, out var p), Is.True, input);
            Assert.That(p.Value, Is.EqualTo(value), input);
            Assert.That(p.From.ToString(), Is.EqualTo(from), input);
            Assert.That(p.To.ToString(), Is.EqualTo(to), input);
        }

        Check("5 km mi", en, 5, "km", "mi");
        Check("5km to mi", en, 5, "km", "mi");
        Check("km mi 5", en, 5, "km", "mi");
        Check("-2.5e3 m", en, -2500, "m", "");
        Check("5eV J", en, 5, "eV", "J");
        Check("10 km in", en, 10, "km", "in");
        Check("1,500 m km", en, 1500, "m", "km");
        Check("1,5 km m", de, 1.5, "km", "m");
        Check("1.5 km m", de, 1.5, "km", "m");
        Check("1.500 m km", de, 1500, "m", "km");
        Check("1_000_000 B MB", en, 1e6, "B", "MB");

        Assert.That(ConvertInputParser.TryParse("km mi", en, out _), Is.False);
        Assert.That(ConvertInputParser.TryParse("5 km to mi now", en, out _), Is.False);
        Assert.That(ConvertInputParser.TryParse("NaN km mi", en, out _), Is.False);
    }

    [Test]
    public void FormattingUsesSignificantDigits()
    {
        var inv = CultureInfo.InvariantCulture;
        var en = CultureInfo.GetCultureInfo("en-US");

        Assert.That(ConvertFormatter.FormatValue(0.1 + 0.2, inv), Is.EqualTo("0.3"));
        Assert.That(ConvertFormatter.FormatValue(1d / 3, inv), Is.EqualTo("0.333333333333"));
        Assert.That(ConvertFormatter.FormatValue(1234567.5, en), Is.EqualTo("1,234,567.5"));
        Assert.That(ConvertFormatter.FormatValue(1e-9, inv), Is.EqualTo("1E-9"));
        Assert.That(ConvertFormatter.FormatValue(6.02214076e23, inv), Is.EqualTo("6.02214076E+23"));
        Assert.That(ConvertFormatter.FormatValue(1e-3 * 1e-3, inv), Is.EqualTo("0.000001"));
    }

    [Test]
    public void ParsersKeepOnlyCurrenciesAndMetals()
    {
        var feed = RatesSources.ParseFeed(JsonDocument.Parse(Feed(1.17)).RootElement)!;
        var rates = CurrencyRates.FromDto(feed);
        Assert.That(rates.Codes, Does.Contain("NGN").And.Contain("XAU").And.Contain("EUR"));
        Assert.That(rates.Codes, Does.Not.Contain("BTC").And.Not.Contain("DEM"));
        Assert.That(CurrencyRates.GetName("XAU"), Is.EqualTo("Gold (troy ounce)"));

        var ecb = RatesSources.ParseFrankfurter(JsonDocument.Parse(ECB_BODY).RootElement)!;
        Assert.That(ecb.Base, Is.EqualTo("EUR"));
        Assert.That(ecb.Rates["USD"], Is.EqualTo(1.17));

        var bad = RatesSources.ParseFeed(JsonDocument.Parse("""{"date":"x","eur":{"usd":1}}""").RootElement);
        Assert.That(bad, Is.Null);
    }

    [Test]
    public async Task RefreshFallsBackAndRejectsBadSnapshots()
    {
        _http.Responses[MIRROR] = Feed(1.17);
        _http.Responses[ECB] = ECB_BODY;
        Assert.That(await _svc.RefreshRatesAsync(), Is.True);
        Assert.That(_svc.Rates.Codes, Does.Contain("NGN"));

        _http.Responses.Remove(MIRROR);
        _http.Responses[FEED] = Feed(2.5);
        Assert.That(await _svc.RefreshRatesAsync(), Is.True);
        Assert.That(_svc.Rates.Codes, Does.Not.Contain("NGN"));
        AssertClose(Ok("EUR", "USD", 1), 1.17);

        _http.Responses.Clear();
        var before = _svc.Rates;
        Assert.That(await _svc.RefreshRatesAsync(), Is.False);
        Assert.That(_svc.Rates, Is.SameAs(before));
    }
}
