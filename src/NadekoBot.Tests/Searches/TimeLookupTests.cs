using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nadeko.Common;
using NadekoBot.Modules.Searches.Common;
using NadekoBot.Modules.Searches.Services;
using NadekoBot.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Searches;

public class TimeLookupTests
{
    private const string ZONE = "Europe/Belgrade";

    private sealed class FakeHandler : HttpMessageHandler
    {
        public int TzCalls;
        public string? TzFailure;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string json;
            if (request.RequestUri!.Host.Contains("locationiq"))
            {
                json = """[{"lat": "44.8125", "lon": "20.4612", "display_name": "Belgrade, Serbia"}]""";
            }
            else
            {
                Interlocked.Increment(ref TzCalls);
                json = TzFailure is { } failure
                    ? $$"""{"status": "FAILED", "message": "{{failure}}"}"""
                    : $$"""{"status": "OK", "message": "", "zoneName": "{{ZONE}}"}""";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private FakeHandler _handler = null!;
    private SearchesService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _handler = new FakeHandler();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_handler, false));

        var creds = Substitute.For<IBotCreds>();
        creds.LocationIqApiKey.Returns("loc-key");
        creds.TimezoneDbApiKey.Returns("tz-key");
        var credsProvider = Substitute.For<IBotCredsProvider>();
        credsProvider.GetCreds().Returns(creds);

        _svc = new SearchesService(Substitute.For<IGoogleApiService>(), new MemoryBotCache(), factory, null!, credsProvider);
    }

    [TearDown]
    public void TearDown()
        => _handler.Dispose();

    [Test]
    public async Task Failures_AreNotCachedAndOnlyKeyProblemsBlameTheKey()
    {
        _handler.TzFailure = "You are over the rate limit";
        var (_, error) = await _svc.GetTimeDataAsync("belgrade");
        Assert.That(error, Is.EqualTo(ErrorType.Unknown));

        _handler.TzFailure = "Invalid API key";
        (_, error) = await _svc.GetTimeDataAsync("belgrade");
        Assert.That(error, Is.EqualTo(ErrorType.ApiKeyInactive));

        _handler.TzFailure = null;
        var (data, ok) = await _svc.GetTimeDataAsync("belgrade");
        Assert.That(ok, Is.Null);
        Assert.That(data.ZoneId, Is.EqualTo(ZONE));
        Assert.That(_handler.TzCalls, Is.EqualTo(3));

        // the zone of a place is cached after a success
        await _svc.GetTimeDataAsync("belgrade");
        Assert.That(_handler.TzCalls, Is.EqualTo(3));
    }
}
