using System;
using System.Text;
using NadekoBot.Hosted;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiFeedBackupTests
{
    private static FeedFrame Frame(long id)
        => new(id, [(byte)id]);

    private static long[] SnapshotIds(FeedRingBuffer buf, long since)
    {
        var frames = new FeedFrame[FeedRingBuffer.CAPACITY];
        var count = buf.Snapshot(since, frames);
        var ids = new long[count];
        for (var i = 0; i < count; i++)
            ids[i] = frames[i].Id;
        return ids;
    }

    [Test]
    public void RingBuffer_ReturnsTheNewestFramesAfterSinceInOrder()
    {
        var buf = new FeedRingBuffer();
        Assert.That(SnapshotIds(buf, 0), Is.Empty);

        for (var i = 1; i <= 5; i++)
            buf.Add(Frame(i));

        Assert.That(SnapshotIds(buf, 0), Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
        Assert.That(SnapshotIds(buf, 3), Is.EqualTo(new long[] { 4, 5 }));
        Assert.That(SnapshotIds(buf, 10), Is.Empty);

        for (var i = 6; i <= 300; i++)
            buf.Add(Frame(i));

        var ids = SnapshotIds(buf, 0);

        Assert.That(ids.Length, Is.EqualTo(FeedRingBuffer.CAPACITY));
        Assert.That(ids[0], Is.EqualTo(300 - FeedRingBuffer.CAPACITY + 1));
        Assert.That(ids[^1], Is.EqualTo(300));
    }

    [Test]
    public void BackupName_IsStrictAndEncodesItsUtcCreationTime()
    {
        Assert.That(BackupArchive.IsValidBackupFileName("nadeko-backup-20260713-142530.zip"), Is.True);
        Assert.That(BackupArchive.CreatedAt("nadeko-backup-20260713-142530.zip"),
            Is.EqualTo(new DateTime(2026, 7, 13, 14, 25, 30, DateTimeKind.Utc)));

        foreach (var name in new[]
                 {
                     "nadeko-backup-20260713-142530.db", "nadeko-backup-2026713-142530.zip",
                     "../nadeko-backup-20260713-142530.zip", "nadeko-backup-20260713-142530.zip\n", "",
                     "nadeko-backup-20261399-999999.zip"
                 })
            Assert.That(BackupArchive.IsValidBackupFileName(name), Is.False, name);
    }

    [Test]
    public void BackupAndRestore_ApplyTheirIncludeLists()
    {
        (string Path, bool BackedUp, bool Restored)[] cases =
        [
            ("bot.yml", true, true),
            ("xp_template.json", true, true),
            ("creds.yml", false, false),
            ("api.yml", false, false),
            ("commandlist.json", false, false),
            ("medusae/mymodule/config.yml", true, false),
            ("medusae/mymodule/mymodule.dll", true, false),
            ("ai/command-embeddings.cache", false, false),
            ("backups/old.zip", false, false),
            ("NadekoBot.db", false, false)
        ];

        foreach (var (path, backedUp, restored) in cases)
        {
            Assert.That(BackupArchive.IsBackupIncluded(path), Is.EqualTo(backedUp), path);
            Assert.That(BackupArchive.IsRestoreIncluded(path), Is.EqualTo(restored), path);
        }
    }
}
