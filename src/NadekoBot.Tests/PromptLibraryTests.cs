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
        Directory.CreateDirectory(Path.Combine(_tempDir, "modules"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "examples"));
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
    public void TryWrite_ValidSoulPath_WritesFile()
    {
        var ok = _lib.TryWrite("SOUL.md", "You are Nadeko.", out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(File.ReadAllText(Path.Combine(_tempDir, "SOUL.md")), Is.EqualTo("You are Nadeko."));
    }

    [Test]
    public void TryWrite_ValidModulePath_WritesFile()
    {
        var ok = _lib.TryWrite("modules/foo.md", "FOO", out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(File.ReadAllText(Path.Combine(_tempDir, "modules", "foo.md")), Is.EqualTo("FOO"));
    }

    [Test]
    public void TryWrite_PathEscape_Rejected()
    {
        var ok = _lib.TryWrite("../escape.md", "evil", out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("escapes"));
    }

    [Test]
    public void TryWrite_ExamplesDir_Rejected()
    {
        var ok = _lib.TryWrite("examples/x.md", "hi", out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("examples"));
    }

    [Test]
    public void TryWrite_NonMdExtension_Rejected()
    {
        var ok = _lib.TryWrite("SOUL.txt", "hi", out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain(".md"));
    }

    [Test]
    public void TryWrite_OversizeModule_Rejected()
    {
        var huge = new string('x', 9 * 1024);

        var ok = _lib.TryWrite("modules/big.md", huge, out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("limit"));
    }

    [Test]
    public async Task ReloadAsync_LoadsFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "SOUL.md"), "soul content");
        File.WriteAllText(Path.Combine(_tempDir, "OPERATOR.md"), "operator content");
        File.WriteAllText(Path.Combine(_tempDir, "modules", "m1.md"), "module one");
        File.WriteAllText(Path.Combine(_tempDir, "modules", "m2.md"), "module two");

        await _lib.ReloadAsync();

        Assert.That(_lib.GetSoul(), Is.EqualTo("soul content"));
        Assert.That(_lib.GetOperatorDoc(), Is.EqualTo("operator content"));
        Assert.That(_lib.ListModules(), Is.EquivalentTo(new[] { "m1", "m2" }));
    }

    [Test]
    public async Task ReloadAsync_EmptyModule_StillListed()
    {
        File.WriteAllText(Path.Combine(_tempDir, "modules", "empty.md"), "   \n");

        await _lib.ReloadAsync();

        Assert.That(_lib.ListModules(), Does.Contain("empty"));
    }

    [Test]
    public async Task GetModules_NullEnabled_ReturnsAll()
    {
        File.WriteAllText(Path.Combine(_tempDir, "modules", "a.md"), "A");
        File.WriteAllText(Path.Combine(_tempDir, "modules", "b.md"), "B");

        await _lib.ReloadAsync();
        var modules = _lib.GetModules(null);

        Assert.That(modules, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetModules_ExplicitEnabled_FiltersCorrectly()
    {
        File.WriteAllText(Path.Combine(_tempDir, "modules", "a.md"), "A");
        File.WriteAllText(Path.Combine(_tempDir, "modules", "b.md"), "B");

        await _lib.ReloadAsync();
        var modules = _lib.GetModules(new[] { "a" });

        Assert.That(modules, Has.Count.EqualTo(1));
        Assert.That(modules[0].Name, Is.EqualTo("a"));
    }

    [Test]
    public async Task ReadRaw_ExistingFile_ReturnsFullContent()
    {
        File.WriteAllText(Path.Combine(_tempDir, "SOUL.md"), "raw content");

        await _lib.ReloadAsync();
        var (content, size) = _lib.ReadRaw("SOUL.md");

        Assert.That(content, Is.EqualTo("raw content"));
        Assert.That(size, Is.EqualTo("raw content".Length));
    }

    [Test]
    public void ReadRaw_MissingFile_ReturnsNull()
    {
        var (content, _) = _lib.ReadRaw("nonexistent.md");

        Assert.That(content, Is.Null);
    }

    [Test]
    public void ReadRaw_PathEscape_ReturnsNull()
    {
        var (content, _) = _lib.ReadRaw("../etc/passwd");

        Assert.That(content, Is.Null);
    }

    [Test]
    public async Task ReloadAsync_OversizeFile_ReadsFullContent()
    {
        var huge = new string('x', 25 * 1024);
        File.WriteAllText(Path.Combine(_tempDir, "SOUL.md"), huge);

        await _lib.ReloadAsync();
        var soul = _lib.GetSoul();

        Assert.That(soul.Length, Is.EqualTo(huge.Length));
    }
}
