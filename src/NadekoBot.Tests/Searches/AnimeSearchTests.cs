using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nadeko.Common;
using NadekoBot.Modules.Searches.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Searches;

public class AnimeSearchTests
{
    private sealed class FakeAnilistHandler : HttpMessageHandler
    {
        public int Calls;
        public readonly List<string> Bodies = [];
        public Func<HttpResponseMessage> Respond = () => new(HttpStatusCode.NotFound);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return Respond();
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json) };

    private FakeAnilistHandler _handler = null!;
    private AnimeSearchService _svc = null!;
    private IPubSub _pubSub = null!;

    [SetUp]
    public void Setup()
    {
        _handler = new FakeAnilistHandler();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_handler, false));
        _pubSub = Substitute.For<IPubSub>();
        _svc = new AnimeSearchService(new MemoryBotCache(), factory, _pubSub);
    }

    [TearDown]
    public void TearDown()
        => _handler.Dispose();

    [Test]
    public async Task Anime_MapsTheAniListMediaAndCachesIt()
    {
        var description = new string('a', 600);
        _handler.Respond = () => Json("""
            {"data":{"Media":{
              "id":154587,"siteUrl":"https://anilist.co/anime/154587","status":"FINISHED",
              "episodes":28,"chapters":null,"volumes":null,"averageScore":91,
              "genres":["Adventure","Drama"],
              "title":{"english":null,"romaji":"Sousou no Frieren"},
              "description":"DESCRIPTION",
              "coverImage":{"extraLarge":"https://img/xl.jpg","large":"https://img/l.jpg"}}}}
            """.Replace("DESCRIPTION", description, StringComparison.Ordinal));

        var anime = await _svc.GetAnimeData("frieren");

        Assert.That(anime, Is.Not.Null);
        Assert.That(anime!.TitleEnglish, Is.EqualTo("Sousou no Frieren"));
        Assert.That(anime.AiringStatus, Is.EqualTo("Finished Airing"));
        Assert.That(anime.TotalEpisodes, Is.EqualTo(28));
        Assert.That(anime.AverageScore, Is.EqualTo(91));
        Assert.That(anime.Genres, Is.EqualTo(new[] { "Adventure", "Drama" }));
        Assert.That(anime.ImageUrlLarge, Is.EqualTo("https://img/xl.jpg"));
        Assert.That(anime.Link, Is.EqualTo("https://anilist.co/anime/154587"));
        Assert.That(anime.Synopsis.Length, Is.LessThanOrEqualTo(500));

        Assert.That(_handler.Bodies[0], Does.Contain("\"type\":\"ANIME\"").And.Contain("\"q\":\"frieren\"")
            .And.Contain("isAdult: false"));

        var again = await _svc.GetAnimeData("  FRIEREN ");
        Assert.That(again?.Id, Is.EqualTo(154587));
        Assert.That(_handler.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Manga_MapsChaptersVolumesStatusAndMissingValues()
    {
        _handler.Respond = () => Json("""
            {"data":{"Media":{
              "id":30002,"siteUrl":"https://anilist.co/manga/30002","status":"RELEASING",
              "episodes":null,"chapters":null,"volumes":41,"averageScore":null,"genres":[],
              "title":{"english":"Berserk","romaji":"Berserk"},"description":null,
              "coverImage":{"extraLarge":null,"large":"https://img/l.jpg"}}}}
            """);

        var manga = await _svc.GetMangaData("berserk");

        Assert.That(manga, Is.Not.Null);
        Assert.That(manga!.TitleEnglish, Is.EqualTo("Berserk"));
        Assert.That(manga.PublishingStatus, Is.EqualTo("Publishing"));
        Assert.That(manga.TotalChapters, Is.EqualTo(0));
        Assert.That(manga.TotalVolumes, Is.EqualTo(41));
        Assert.That(manga.AverageScore, Is.EqualTo(0));
        Assert.That(manga.ImageUrlLge, Is.EqualTo("https://img/l.jpg"));
        Assert.That(manga.Synopsis, Is.Empty);
        Assert.That(_handler.Bodies[0], Does.Contain("\"type\":\"MANGA\""));
    }

    [Test]
    public async Task Failures_ReturnNullAndARateLimitPausesTheSearches()
    {
        _handler.Respond = () => Json("""{"errors":[{"message":"Not Found.","status":404}],"data":{"Media":null}}""",
            HttpStatusCode.NotFound);
        Assert.That(await _svc.GetAnimeData("no such anime"), Is.Null);

        _handler.Respond = () => Json("""{"data":{"Media":null}}""");
        Assert.That(await _svc.GetMangaData("no such manga"), Is.Null);

        _handler.Respond = () => Json("not json");
        Assert.That(await _svc.GetMangaData("broken"), Is.Null);

        _handler.Respond = () => throw new HttpRequestException("network down");
        Assert.That(await _svc.GetAnimeData("offline"), Is.Null);

        _handler.Respond = () =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            res.Headers.RetryAfter = new(TimeSpan.FromSeconds(60));
            return res;
        };
        Assert.That(await _svc.GetAnimeData("limited"), Is.Null);

        var callsAfterLimit = _handler.Calls;
        _handler.Respond = () => Json("""{"data":{"Media":{"id":1,"title":{"romaji":"x"}}}}""");
        Assert.That(await _svc.GetAnimeData("limited"), Is.Null);
        Assert.That(await _svc.GetAnilistUserAsync("someone"), Is.Null);
        Assert.That(_handler.Calls, Is.EqualTo(callsAfterLimit));
        await _pubSub.Received(1).Pub(Arg.Any<TypedKey<long>>(), 60_000L);
    }
}
