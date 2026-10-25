using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NadekoBot.Modules.Music;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class WebmOpusDemuxerTests
{
    public static string GetNyanpassuClipPath()
        => Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "nyanpassu_clip.webm");

    public static async Task<List<byte[]>> ReadAllPacketsAsync(Stream stream)
    {
        using var demuxer = new WebmOpusDemuxer(stream);
        Assert.That(await demuxer.InitializeAsync(), Is.True);
        Assert.That(demuxer.IsOpus, Is.True);

        var packets = new List<byte[]>();
        while (await demuxer.ReadPacketAsync() > 0)
            packets.Add(demuxer.Packet.ToArray());

        return packets;
    }

    [Test]
    public async Task ReadsEveryPacketOfTheClip()
    {
        var packets = await ReadAllPacketsAsync(WebmOpusDemuxer.OpenFile(GetNyanpassuClipPath()));

        // ffprobe: 25 packets of 20 ms.
        Assert.That(packets, Has.Count.EqualTo(25));
        Assert.That(packets.All(p => p.Length is > 0 and < 2000), Is.True);
        Assert.That(packets.All(p => OpusPacket.GetSamplesPerChannel(p) == 960), Is.True);
    }

    [Test]
    public async Task NetworkStreamGivesTheSamePacketsAndOtherFormatsAreRefused()
    {
        var bytes = await File.ReadAllBytesAsync(GetNyanpassuClipPath());
        var fromFile = await ReadAllPacketsAsync(new MemoryStream(bytes));
        var fromTrickle = await ReadAllPacketsAsync(new TrickleStream(bytes));

        Assert.That(fromTrickle, Has.Count.EqualTo(fromFile.Count));
        for (var i = 0; i < fromFile.Count; i++)
            Assert.That(fromTrickle[i], Is.EqualTo(fromFile[i]), $"packet {i}");

        // An m4a file starts with an ftyp box, not an EBML header.
        var m4a = new byte[] { 0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', 0x20 };
        foreach (var other in new[] { m4a, [], bytes[..40] })
        {
            using var demuxer = new WebmOpusDemuxer(new MemoryStream(other));
            Assert.That(await demuxer.InitializeAsync(), Is.False);
        }
    }

    // Gives 1 to 7 bytes for each read, as a slow network can.
    public sealed class TrickleStream(byte[] data) : Stream
    {
        private int _position;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var n = Math.Min(Math.Min(buffer.Length, 1 + (_position % 7)), data.Length - _position);
            data.AsSpan(_position, n).CopyTo(buffer.Span);
            _position += n;
            return ValueTask.FromResult(n);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
