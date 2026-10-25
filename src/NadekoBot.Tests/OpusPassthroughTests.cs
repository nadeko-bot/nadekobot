#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Searches;
using NadekoBot.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class OpusPassthroughTests
{
    private const string BASE_URL = "http://10.0.0.5:8080";
    private const string KEY = "nadeko-public.test-signature";
    private const string VIDEO_ID = "dQw4w9WgXcQ";
    private const int FRAME_SAMPLES = 960;

    [Test]
    public void PacketRing_KeepsOrderAcrossWrapsAndRefusesWhenFull()
    {
        const int capacity = 1 << 17;
        using var ring = new OpusPacketRing(capacity);
        var output = new byte[OpusPacketRing.MAX_PACKET_SIZE];

        // Two of the largest entries fill the ring exactly.
        var big = new byte[OpusPacketRing.MAX_PACKET_SIZE];
        Assert.That(ring.TryWrite(big) && ring.TryWrite(big), Is.True);
        Assert.That(ring.TryWrite([1]), Is.False, "the ring is full");
        Assert.That(ring.TryRead(output), Is.EqualTo(big.Length));
        Assert.That(ring.TryRead(output), Is.EqualTo(big.Length));
        Assert.That(ring.TryRead(output), Is.Zero);

        var rng = new Random(5);
        var written = new List<byte[]>();
        var producer = Task.Run(() =>
        {
            for (var i = 0; i < 20_000; i++)
            {
                var packet = new byte[rng.Next(1, 1500)];
                rng.NextBytes(packet);
                packet[0] = (byte)i;
                lock (written)
                    written.Add(packet);

                while (!ring.TryWrite(packet))
                    Thread.Yield();
            }
        });

        var read = 0;
        while (read < 20_000)
        {
            var length = ring.TryRead(output);
            if (length == 0)
            {
                Thread.Yield();
                continue;
            }

            byte[] expected;
            lock (written)
                expected = written[read];

            Assert.That(output.AsSpan(0, length).SequenceEqual(expected), Is.True, $"packet {read}");
            read++;
        }

        Assert.That(producer.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(ring.Count, Is.Zero);
    }

    [Test]
    public async Task Source_ReadsAheadAndRefusesOtherFrameLengths()
    {
        var path = WebmOpusDemuxerTests.GetNyanpassuClipPath();

        Assert.That(await OpusStreamSource.OpenAsync(WebmOpusDemuxer.OpenFile(path), FRAME_SAMPLES * 2, default), Is.Null,
            "20 ms packets do not fit the 40 ms frames of the medium preset");
        Assert.That(await OpusStreamSource.OpenAsync(new MemoryStream(new byte[64]), FRAME_SAMPLES, default), Is.Null);

        await using var source = await OpusStreamSource.OpenAsync(WebmOpusDemuxer.OpenFile(path), FRAME_SAMPLES, default);
        Assert.That(source, Is.Not.Null);
        Assert.That(source!.WaitBufferedAsync().Wait(TimeSpan.FromSeconds(5)), Is.True);

        var expected = await WebmOpusDemuxerTests.ReadAllPacketsAsync(WebmOpusDemuxer.OpenFile(path));
        Assert.That(await ReadAllInternalAsync(source), Is.EqualTo(expected));
    }

    [Test]
    public async Task ServiceStream_ResumesFromTheLastByteAndLeavesOtherAudioToFfmpeg()
    {
        var clip = await File.ReadAllBytesAsync(WebmOpusDemuxerTests.GetNyanpassuClipPath());
        var cut = clip.Length / 2;
        var requests = new List<(string Path, string? Auth, RangeHeaderValue? Range)>();
        var contentType = "audio/webm";

        using var handler = new Handler(req =>
        {
            lock (requests)
                requests.Add((req.RequestUri!.PathAndQuery, req.Headers.Authorization?.ToString(), req.Headers.Range));

            if (req.Headers.Range?.Ranges is { Count: 1 } ranges && ranges.GetEnumerator() is var e && e.MoveNext()
                && e.Current.From is { } from)
            {
                var rest = new ByteArrayContent(clip, (int)from, clip.Length - (int)from);
                rest.Headers.ContentRange = new ContentRangeHeaderValue(from, clip.Length - 1, clip.Length);
                rest.Headers.ContentType = new(contentType);
                return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = rest };
            }

            var first = new StreamContent(new CutStream(clip, cut));
            first.Headers.ContentLength = clip.Length;
            first.Headers.ContentType = new(contentType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = first };
        });

        var client = CreateClientInternal(handler);
        var url = client.GetAudioUrl(VIDEO_ID)!;

        await using (var source = await OpusStreamSource.OpenAsync((await client.OpenAudioAsync(url, default))!, FRAME_SAMPLES, default))
        {
            Assert.That(source, Is.Not.Null);
            var expected = await WebmOpusDemuxerTests.ReadAllPacketsAsync(new MemoryStream(clip));
            Assert.That(await ReadAllInternalAsync(source!), Is.EqualTo(expected));
        }

        Assert.That(requests, Has.Count.EqualTo(2));
        Assert.That(requests, Has.All.Matches<(string Path, string? Auth, RangeHeaderValue?)>(
            r => r.Path == "/v1/audio/" + VIDEO_ID && r.Auth == "Bearer " + KEY));
        Assert.That(requests[1].Range?.ToString(), Is.EqualTo($"bytes={cut}-"));

        contentType = "audio/mp4";
        Assert.That(await client.OpenAudioAsync(url, default), Is.Null);

        var calls = requests.Count;
        Assert.That(await client.OpenAudioAsync("http://10.0.0.6:8080/v1/audio/" + VIDEO_ID, default), Is.Null);
        Assert.That(requests, Has.Count.EqualTo(calls), "the key goes only to the service");
    }

    private static async Task<List<byte[]>> ReadAllInternalAsync(OpusStreamSource source)
    {
        var packets = new List<byte[]>();
        var output = new byte[OpusPacketRing.MAX_PACKET_SIZE];
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            switch (source.TryRead(output, out var length))
            {
                case OpusReadResult.Packet:
                    packets.Add(output[..length]);
                    break;
                case OpusReadResult.Starved:
                    await Task.Delay(5);
                    break;
                default:
                    return packets;
            }
        }

        Assert.Fail("the source did not end");
        return packets;
    }

    private static MusicServiceClient CreateClientInternal(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, false));

        var config = new SearchesConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));
        config.ModifyConfig(c => c.MusicServiceUrl = BASE_URL);
        return new MusicServiceClient(factory, config, new BotPolicy(false)) { Key = KEY };
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    // Gives the first bytes of data, then breaks like a dropped connection.
    private sealed class CutStream(byte[] data, int cut) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= cut)
                throw new IOException("connection reset");

            var n = Math.Min(count, cut - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
