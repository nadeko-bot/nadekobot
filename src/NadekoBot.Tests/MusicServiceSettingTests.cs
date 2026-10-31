#nullable enable
using System;
using System.IO;
using NadekoBot.Modules.Music;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class MusicServiceSettingTests
{
    private const string URL = "http://10.0.0.5:8080";

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko_ms_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
        => Directory.Delete(_dir, true);

    private static MusicServiceSetting.Entry Entry(char keyChar)
    {
        Assert.That(MusicServiceSetting.TryCreate("bot1", URL, new string(keyChar, MusicServiceSetting.MIN_KEY_LENGTH), out var entry), Is.True);
        return entry;
    }

    [Test]
    public void Setting_SeesEachWriteAtOnceAndRefusesAnOversizedFile()
    {
        var setting = new MusicServiceSetting(Path.Combine(_dir, ".music-service"));
        var stale = setting.FilePath + ".old.tmp";
        var fresh = setting.FilePath + ".new.tmp";
        File.WriteAllText(stale, "key=leftover");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddMinutes(-5));
        File.WriteAllText(fresh, "key=being-written");

        setting.Set(Entry('a'));
        Assert.That(setting.Current?.Key, Is.EqualTo(new string('a', MusicServiceSetting.MIN_KEY_LENGTH)));
        Assert.That((File.Exists(stale), File.Exists(fresh)), Is.EqualTo((false, true)), "only a stopped write's file goes");

        // The same length and the same file time: the bot still sees the new key.
        var stamp = File.GetLastWriteTimeUtc(setting.FilePath);
        setting.Set(Entry('b'));
        File.SetLastWriteTimeUtc(setting.FilePath, stamp);
        Assert.That(setting.Current?.Key, Is.EqualTo(new string('b', MusicServiceSetting.MIN_KEY_LENGTH)));

        setting.Clear();
        Assert.That(setting.Current, Is.Null);

        var valid = $"id=bot1\nurl={URL}\nkey={new string('c', MusicServiceSetting.MIN_KEY_LENGTH)}\n";
        File.WriteAllText(setting.FilePath, valid + new string('#', MusicServiceSetting.MAX_FILE_BYTES));
        Assert.That(setting.Current, Is.Null);

        File.WriteAllText(setting.FilePath, valid + new string('#', MusicServiceSetting.MAX_FILE_BYTES - valid.Length));
        Assert.That(setting.Current?.Key, Is.EqualTo(new string('c', MusicServiceSetting.MIN_KEY_LENGTH)), "exactly the limit");
    }

    [Test]
    public void For_GivesTheBotsInstanceForTheBotsFile()
    {
        Assert.That(MusicServiceSetting.For(MusicServiceSetting.Default.FilePath), Is.SameAs(MusicServiceSetting.Default));
        Assert.That(MusicServiceSetting.For(Path.Combine("data", ".music-service")), Is.SameAs(MusicServiceSetting.Default));
        Assert.That(MusicServiceSetting.For(Path.Combine(_dir, ".music-service")), Is.Not.SameAs(MusicServiceSetting.Default));
    }
}
