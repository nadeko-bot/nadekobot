#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Common.Yml;
using NadekoBot.Hosted;
using NadekoBot.Modules.Searches.Services;
using NadekoBot.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Searches;

public class MovieSearchTests
{
    private const string MATRIX = """
        {"Title":"The Matrix","Year":"1999","imdbRating":"8.7","imdbID":"tt0133093","Genre":"Action, Sci-Fi",
         "Plot":"A hacker learns the truth.","Poster":"https://img/matrix.jpg","Response":"True"}
        """;

    private sealed class FakeOmdbHandler : HttpMessageHandler
    {
        public int Calls;
        public Uri? LastUri;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = MATRIX;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }

    private FakeOmdbHandler _handler = null!;
    private IHttpClientFactory _factory = null!;
    private string _dir = null!;

    [SetUp]
    public void Setup()
    {
        _handler = new FakeOmdbHandler();
        _factory = Substitute.For<IHttpClientFactory>();
        _factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_handler, false));
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-movie-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
        Directory.Delete(_dir, true);
    }

    private MovieService CreateService(string envKey, string credsKey)
    {
        var creds = Substitute.For<IBotCreds>();
        creds.OmdbApiKey.Returns(credsKey);
        var provider = Substitute.For<IBotCredsProvider>();
        provider.GetCreds().Returns(creds);
        return new MovieService(_factory, new MemoryBotCache(), provider) { EnvKey = envKey };
    }

    [Test]
    public async Task WithoutAKey_ReturnsMissingKeyAndSendsNoRequest()
    {
        var result = await CreateService("", "  ").GetMovieDataAsync("the matrix");

        Assert.That(result.TryPickT1(out var error, out _), Is.True);
        Assert.That(error, Is.EqualTo(MovieError.MissingKey));
        Assert.That(_handler.Calls, Is.Zero);
    }

    [Test]
    public async Task TheEnvironmentKeyWinsOverCredsAndTheFieldsMap()
    {
        var result = await CreateService("env-key", "creds-key").GetMovieDataAsync("  The Matrix ");

        Assert.That(result.TryPickT0(out var movie, out _), Is.True);
        Assert.That(movie.Title, Is.EqualTo("The Matrix"));
        Assert.That(movie.ImdbId, Is.EqualTo("tt0133093"));
        Assert.That(movie.ImdbRating, Is.EqualTo("8.7"));
        Assert.That(movie.Year, Is.EqualTo("1999"));
        Assert.That(movie.Poster, Is.EqualTo("https://img/matrix.jpg"));
        Assert.That(_handler.LastUri!.Host, Is.EqualTo("www.omdbapi.com"));
        Assert.That(_handler.LastUri.Query, Does.Contain("apikey=env-key").And.Contain("t=The%20Matrix"));

        var svc = CreateService("env-key", "");
        await svc.GetMovieDataAsync("The Matrix");
        await svc.GetMovieDataAsync("  the matrix ");
        Assert.That(_handler.Calls, Is.EqualTo(2));

        await CreateService("", "creds-key").GetMovieDataAsync("other");
        Assert.That(_handler.LastUri!.Query, Does.Contain("apikey=creds-key"));
    }

    [Test]
    public async Task FailuresMapToTheirErrorAndAreNotCached()
    {
        var svc = CreateService("key", "");

        _handler.Body = """{"Response":"False","Error":"Movie not found!"}""";
        Assert.That((await svc.GetMovieDataAsync("nothing")).AsT1, Is.EqualTo(MovieError.NotFound));

        _handler.Status = HttpStatusCode.Unauthorized;
        _handler.Body = """{"Response":"False","Error":"Request limit reached!"}""";
        Assert.That((await svc.GetMovieDataAsync("the matrix")).AsT1, Is.EqualTo(MovieError.KeyRefused));

        _handler.Body = "<html>unauthorized</html>";
        Assert.That((await svc.GetMovieDataAsync("the matrix")).AsT1, Is.EqualTo(MovieError.KeyRefused));

        _handler.Status = HttpStatusCode.BadGateway;
        _handler.Body = "<html>bad gateway</html>";
        Assert.That((await svc.GetMovieDataAsync("the matrix")).AsT1, Is.EqualTo(MovieError.NotFound));

        _handler.Status = HttpStatusCode.OK;
        _handler.Body = MATRIX;
        Assert.That((await svc.GetMovieDataAsync("the matrix")).IsT0, Is.True);
    }

    [Test]
    public void CredsWrites_KeepTheOmdbKey()
    {
        var path = Path.Combine(_dir, "creds.yml");
        File.WriteAllText(path, Yaml.Serializer.Serialize(new Creds { Token = "old", OmdbApiKey = "omdb-key" }));

        CredsFile.WriteToken(path, "new");

        Assert.That(File.ReadAllText(path), Does.Contain("omdbApiKey: omdb-key"));
        Assert.That(CredsFile.Read(path)!.OmdbApiKey, Is.EqualTo("omdb-key"));
    }
}
