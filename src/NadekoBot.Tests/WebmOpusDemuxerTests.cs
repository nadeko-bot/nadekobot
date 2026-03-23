using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NadekoBot.Modules.Music;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class WebmOpusDemuxerTests
{
    private static string GetNyanpassuClipPath()
        => Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "nyanpassu_clip.webm");

    [Test]
    public void Initialize_DetectsOpus_AndReadsFirstPacket()
    {
        var path = GetNyanpassuClipPath();
        Assert.That(File.Exists(path), Is.True, $"Test file not found at {path}");

        using var demuxer = new WebmOpusDemuxer(path);
        Assert.That(demuxer.Initialize(), Is.True);
        Assert.That(demuxer.IsOpus, Is.True);

        var result = demuxer.TryReadPacket(out var data, out var length);
        Assert.That(result, Is.True);
        Assert.That(length, Is.GreaterThan(0));
        Assert.That(length, Is.LessThan(4000));
    }

    [Test]
    public void ReadAllPackets_ReasonableCountAndSizes()
    {
        using var demuxer = new WebmOpusDemuxer(GetNyanpassuClipPath());
        demuxer.Initialize();

        var sizes = new List<int>();
        while (demuxer.TryReadPacket(out _, out var length))
            sizes.Add(length);

        // 0.5s clip at 20ms per frame = ~25 packets
        Assert.That(sizes.Count, Is.GreaterThan(5));
        Assert.That(sizes.Count, Is.LessThan(100));
        Assert.That(sizes.All(s => s is > 0 and < 2000), Is.True,
            $"Unexpected packet sizes: {string.Join(", ", sizes)}");
    }
}
