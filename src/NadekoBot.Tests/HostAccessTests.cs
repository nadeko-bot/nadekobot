#nullable enable
using System;
using System.Buffers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Common.Attributes;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Administration;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Music.Resolvers;
using NadekoBot.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class HostAccessTests
{
    [Test]
    public void IsPublicAddress_RejectsEveryLocalRange()
    {
        string[] local =
        [
            "127.0.0.1", "10.1.2.3", "172.31.0.1", "192.168.1.1", "169.254.169.254", "100.64.0.1", "0.0.0.0",
            "224.0.0.1", "255.255.255.255", "198.18.0.1", "::1", "::", "::ffff:127.0.0.1", "::ffff:10.0.0.1",
            "fe80::1", "fd00::1", "fc00::1", "ff02::1", "2001:db8::1", "2001:0:4136:e378::1", "2002:0a00:0001::1"
        ];
        string[] global = ["1.1.1.1", "8.8.8.8", "100.128.0.1", "::ffff:8.8.8.8", "2606:4700:4700::1111", "2002:0808:0808::1"];

        Assert.That(local.Where(x => UrlExtensions.IsPublicAddress(IPAddress.Parse(x))), Is.Empty);
        Assert.That(global.Where(x => !UrlExtensions.IsPublicAddress(IPAddress.Parse(x))), Is.Empty);
        Assert.That(UrlExtensions.IsPublicUrl("http://[::ffff:7f00:1]/"), Is.False);
        Assert.That(UrlExtensions.IsPublicUrl("file:///etc/passwd"), Is.False);
    }

    [Test]
    public void Ytdl_RefusesLocalTargets()
    {
        string[] refused = ["http://127.0.0.1:8080/a.mp3", "https://192.168.1.5/x", "ftp://10.0.0.1/f", "file:///etc/passwd"];
        foreach (var target in refused)
            Assert.That(YtdlOperation.IsAllowedTarget(target, false), Is.False, target);

        Assert.That(YtdlOperation.IsAllowedTarget("127.0.0.1/a.mp3", true), Is.False);
        Assert.That(YtdlOperation.IsAllowedTarget("localhost/x", true), Is.True);
        Assert.That(YtdlOperation.IsAllowedTarget("10.0.0.1/a lofi mix", false), Is.True);
        Assert.That(YtdlOperation.IsAllowedTarget("https://www.youtube.com/watch?v=dQw4w9WgXcQ", false), Is.True);
        Assert.That(YtdlOperation.IsAllowedTarget("-f", true), Is.True);
    }

    [Test]
    public void StreamUrls_MustBePublicUnlessTheOwnerQueuedThem()
    {
        Assert.That(MusicPlayer.IsAllowedStreamUrl(MusicPlatform.Local, false, "/music/a.mp3"), Is.False);
        Assert.That(MusicPlayer.IsAllowedStreamUrl(MusicPlatform.Local, true, "/music/a.mp3"), Is.True);
        Assert.That(MusicPlayer.IsAllowedStreamUrl(MusicPlatform.Youtube, true, "/data/a.mp3"), Is.False);
        Assert.That(MusicPlayer.IsAllowedStreamUrl(MusicPlatform.Radio, false, "http://10.0.0.5/live"), Is.False);
        Assert.That(MusicPlayer.IsAllowedStreamUrl(MusicPlatform.Radio, true, "http://10.0.0.5/live"), Is.True);
        Assert.That(MusicPlayer.IsAllowedStreamUrl(MusicPlatform.Youtube, false, "https://1.1.1.1/v.webm"), Is.True);
    }

    [Test]
    public void LocalNetwork_OnlyForTheRealOwnerOfASelfHostedBot()
    {
        var owner = Substitute.For<IUser>();
        owner.Id.Returns(1UL);
        var member = Substitute.For<IUser>();
        member.Id.Returns(2UL);
        var creds = Substitute.For<IBotCreds>();
        creds.OwnerIds.Returns(new[] { 1UL });

        var ownerMsg = Substitute.For<IUserMessage>();
        ownerMsg.Author.Returns(owner);
        var memberMsg = Substitute.For<IUserMessage>();
        memberMsg.Author.Returns(member);
        var agentMsg = new DoAsUserMessage(ownerMsg, owner, ".play http://192.168.1.5/radio")
        {
            BlocksOwnerCommands = true
        };

        var selfHosted = new BotPolicy(false);
        Assert.That(selfHosted.CanReachLocalNetwork(creds, ownerMsg), Is.True);
        Assert.That(selfHosted.CanReachLocalNetwork(creds, memberMsg), Is.False);
        Assert.That(selfHosted.CanReachLocalNetwork(creds, agentMsg), Is.False);
        Assert.That(new BotPolicy(true).CanReachLocalNetwork(creds, ownerMsg), Is.False);
        Assert.That(BotPolicy.IsRealOwner(creds, ownerMsg), Is.True);
        Assert.That(BotPolicy.IsRealOwner(creds, agentMsg), Is.False);
    }

    [Test]
    public async Task LocalTracks_OnlyWhenLocalAccessIsAllowed()
    {
        var local = Substitute.For<ILocalTrackResolver>();
        var provider = new TrackResolveProvider(Substitute.For<IYoutubeResolverFactory>(),
            local,
            Substitute.For<IRadioResolver>());

        Assert.That(await provider.QuerySongAsync("/etc/passwd.mp3", MusicPlatform.Local, false), Is.Null);
        await local.DidNotReceiveWithAnyArgs().ResolveByQueryAsync(default!);

        await provider.QuerySongAsync("/music/a.mp3", MusicPlatform.Local, true);
        await local.Received(1).ResolveByQueryAsync("/music/a.mp3");
    }

    [Test]
    public async Task ImageCache_RejectsImagesOverThePixelLimit()
    {
        static byte[] Gif(ushort width, ushort height)
        {
            using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1);
            using var ms = new System.IO.MemoryStream();
            SixLabors.ImageSharp.ImageExtensions.SaveAsGif(img, ms);
            var bytes = ms.ToArray();
            // The logical screen size follows the 6 byte "GIF89a" header.
            BitConverter.TryWriteBytes(bytes.AsSpan(6), width);
            BitConverter.TryWriteBytes(bytes.AsSpan(8), height);
            return bytes;
        }

        Assert.That(ImageCache.IsWithinPixelLimit(Gif(16, 16)), Is.True);
        Assert.That(ImageCache.IsWithinPixelLimit(Gif(5000, 5000)), Is.False);

        var path = System.IO.Path.GetTempFileName();
        try
        {
            await System.IO.File.WriteAllBytesAsync(path, Gif(5000, 5000));
            var cache = new ImageCache(new MemoryBotCache(),
                new ImagesConfig(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false)),
                Substitute.For<IHttpClientFactory>(),
                new BotPolicy(false));

            var url = new Uri(path);
            Assert.That(await cache.GetImageDataAsync(url), Is.Null);

            // The rejected result is cached, so the bot does not read the file again.
            System.IO.File.Delete(path);
            Assert.That(await cache.GetImageDataAsync(url), Is.Null);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test]
    public async Task AliasText_NeverRunsAsTheOwner()
    {
        var owner = Substitute.For<IUser>();
        owner.Id.Returns(1UL);
        var creds = Substitute.For<IBotCreds>();
        creds.OwnerIds.Returns(new[] { 1UL });

        var alias = Substitute.For<IInputTransformer>();
        alias.TransformInput(Arg.Any<IGuild?>(), Arg.Any<IMessageChannel>(), Arg.Any<IUser>(), "hi")
             .Returns(new ValueTask<string?>(".sqlexec DELETE FROM Quotes"));

        var handler = new BehaviorHandler(new ServiceCollection()
                                          .AddSingleton(alias)
                                          .BuildServiceProvider());
        handler.Initialize();

        IUserMessage Typed(string text)
        {
            var msg = Substitute.For<IUserMessage>();
            msg.Author.Returns(owner);
            msg.Content.Returns(text);
            return msg;
        }

        var plain = Typed(".h");
        Assert.That(await handler.RunInputTransformersAsync(null, plain), Is.SameAs(plain));

        var aliased = await handler.RunInputTransformersAsync(null, Typed("hi"));
        Assert.That(aliased.Content, Is.EqualTo(".sqlexec DELETE FROM Quotes"));
        Assert.That(aliased.Author, Is.SameAs(owner));
        Assert.That(BotPolicy.IsRealOwner(creds, aliased), Is.False);
    }

    [Test]
    public async Task ExternalClient_DoesNotConnectToTheLocalNetwork()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var http = new HttpClient(ExternalHttp.CreateHandler());

            foreach (var url in new[] { $"http://127.0.0.1:{port}/", $"http://localhost:{port}/" })
                Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(url), url);

            await Task.Delay(100);
            Assert.That(listener.Pending(), Is.False);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task ReadLimited_ReturnsNullOverTheLimit()
    {
        var data = new byte[200_000];
        Random.Shared.NextBytes(data);

        Assert.That(await ExternalHttp.ReadLimitedAsync(new ByteArrayContent(data), 150_000), Is.Null);

        var read = await ExternalHttp.ReadLimitedAsync(new StreamContent(new NonSeekableStream(data)), 200_000);
        Assert.That(read, Is.Not.Null);
        Assert.That(read!.Value.Buffer.AsSpan(0, read.Value.Length).SequenceEqual(data), Is.True);
        ArrayPool<byte>.Shared.Return(read.Value.Buffer);

        Assert.That(await ExternalHttp.ReadLimitedAsync(new StreamContent(new NonSeekableStream(data)), 199_999),
            Is.Null);
    }

    [Test]
    public async Task OwnerAndManagedOnlyCommands_RefuseTextChosenByOthers()
    {
        var owner = Substitute.For<IUser>();
        owner.Id.Returns(1UL);
        var creds = Substitute.For<IBotCreds>();
        creds.OwnerIds.Returns(new[] { 1UL });
        var credsProvider = Substitute.For<IBotCredsProvider>();
        credsProvider.GetCreds().Returns(creds);

        IServiceProvider Services(bool managed)
            => new ServiceCollection()
               .AddSingleton(credsProvider)
               .AddSingleton(new BotPolicy(managed))
               .BuildServiceProvider();

        ICommandContext Context(bool blocksOwner)
        {
            var ctx = Substitute.For<ICommandContext>();
            ctx.User.Returns(owner);
            ctx.Message.Returns(new DoAsUserMessage(Substitute.For<IUserMessage>(), owner, ".die")
            {
                BlocksOwnerCommands = blocksOwner
            });
            return ctx;
        }

        var ownerOnly = new OwnerOnlyAttribute();
        Assert.That((await ownerOnly.CheckPermissionsAsync(Context(false), null!, Services(false))).IsSuccess, Is.True);
        Assert.That((await ownerOnly.CheckPermissionsAsync(Context(true), null!, Services(false))).IsSuccess, Is.False);

        var noManaged = new NoManagedBotAttribute();
        Assert.That((await noManaged.CheckPermissionsAsync(Context(false), null!, Services(false))).IsSuccess, Is.True);
        Assert.That((await noManaged.CheckPermissionsAsync(Context(false), null!, Services(true))).IsSuccess, Is.False);
    }

    private sealed class NonSeekableStream(byte[] data) : System.IO.MemoryStream(data)
    {
        public override bool CanSeek
            => false;
    }
}
