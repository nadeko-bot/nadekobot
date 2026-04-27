using System.IO;
using System.Threading.Tasks;
using Nadeko.Common;
using NadekoBot.Modules.Utility.AiAgent.Prompts;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class PromptLibraryTests
{
    private string _tempDir = null!;
    private PromptLibrary _lib = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nadeko-prompt-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _lib = new PromptLibrary(new EventPubSub(), _tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Test]
    public void TryWrite_Soul_WritesFile()
    {
        var ok = _lib.TryWrite(PromptKind.Soul, "You are Nadeko.", out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(File.ReadAllText(Path.Combine(_tempDir, "SOUL.md")), Is.EqualTo("You are Nadeko."));
    }

    [Test]
    public void TryWrite_Operator_WritesFile()
    {
        var ok = _lib.TryWrite(PromptKind.Operator, "Be helpful.", out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(File.ReadAllText(Path.Combine(_tempDir, "OPERATOR.md")), Is.EqualTo("Be helpful."));
    }

    [Test]
    public void TryWrite_OversizeContent_Rejected()
    {
        var huge = new string('x', 25 * 1024);

        var ok = _lib.TryWrite(PromptKind.Soul, huge, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("limit"));
    }

    [Test]
    public async Task ReloadAsync_LoadsFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "SOUL.md"), "soul content");
        File.WriteAllText(Path.Combine(_tempDir, "OPERATOR.md"), "operator content");

        await _lib.ReloadAsync();

        Assert.That(_lib.GetSoul(), Is.EqualTo("soul content"));
        Assert.That(_lib.GetOperatorDoc(), Is.EqualTo("operator content"));
    }

    [Test]
    public async Task Read_ReturnsCurrentSnapshot()
    {
        File.WriteAllText(Path.Combine(_tempDir, "SOUL.md"), "raw content");

        await _lib.ReloadAsync();

        Assert.That(_lib.Read(PromptKind.Soul), Is.EqualTo("raw content"));
    }

    [Test]
    public async Task Read_MissingFile_ReturnsEmpty()
    {
        await _lib.ReloadAsync();

        Assert.That(_lib.Read(PromptKind.Operator), Is.Empty);
    }

    [Test]
    public async Task ReloadAsync_OversizeFileOnDisk_ReadsFullContent()
    {
        var huge = new string('x', 25 * 1024);
        File.WriteAllText(Path.Combine(_tempDir, "SOUL.md"), huge);

        await _lib.ReloadAsync();

        Assert.That(_lib.GetSoul().Length, Is.EqualTo(huge.Length));
    }
}
