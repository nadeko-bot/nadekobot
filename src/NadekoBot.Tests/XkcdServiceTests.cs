using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NadekoBot.Modules.Searches;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class XkcdServiceTests
{
    private const int LATEST = 3302;

    private sealed class FakeXkcdHandler : HttpMessageHandler
    {
        public int LatestCalls;
        public bool LatestFails;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            int num;
            if (path == "/info.0.json")
            {
                Interlocked.Increment(ref LatestCalls);
                if (LatestFails)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

                num = LATEST;
            }
            else
            {
                num = int.Parse(path.Split('/', StringSplitOptions.RemoveEmptyEntries)[0]);
                if (num == 404 || num > LATEST)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var json = $$"""{"num": {{num}}, "day": "1", "month": "9", "year": "2026", "safe_title": "T", "img": "i", "alt": "a"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private FakeXkcdHandler _handler = null!;
    private XkcdService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _handler = new FakeXkcdHandler();
        _svc = CreateService(_handler);
    }

    [TearDown]
    public void TearDown()
        => _handler.Dispose();

    private static XkcdService CreateService(FakeXkcdHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, false));
        return new XkcdService(factory);
    }

    [Test]
    public async Task Random_CoversAllComicsAlsoWhenTheLatestFails_AndSpecificReturnsNullForMissingComics()
    {
        var sawNewComic = false;
        for (var i = 0; i < 200; i++)
        {
            var comic = await _svc.GetRandomAsync();
            Assert.That(comic, Is.Not.Null);
            Assert.That(comic!.Num, Is.InRange(1, LATEST));
            sawNewComic |= comic.Num > 1750;
        }

        Assert.That(sawNewComic, Is.True);
        Assert.That(_handler.LatestCalls, Is.EqualTo(1));

        Assert.That((await _svc.GetComicAsync(1400))?.Num, Is.EqualTo(1400));
        Assert.That(await _svc.GetComicAsync(404), Is.Null);
        Assert.That(await _svc.GetComicAsync(LATEST + 1), Is.Null);
        Assert.That(await _svc.GetComicAsync(0), Is.Null);

        using var failing = new FakeXkcdHandler { LatestFails = true };
        var failingSvc = CreateService(failing);
        for (var i = 0; i < 20; i++)
        {
            var comic = await failingSvc.GetRandomAsync();
            Assert.That(comic, Is.Not.Null);
            Assert.That(comic!.Num, Is.InRange(1, LATEST));
        }

        Assert.That(failing.LatestCalls, Is.EqualTo(1));
    }
}
