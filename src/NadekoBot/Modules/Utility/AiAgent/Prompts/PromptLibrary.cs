using System.Collections.Frozen;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Utility.AiAgent.Prompts;

public sealed class PromptLibrary : INService, IReadyExecutor
{
    public const string DEFAULT_PROMPTS_DIR = "data/ai/prompts";
    private const string SOUL_FILE = "SOUL.md";
    private const string OPERATOR_FILE = "OPERATOR.md";
    private const string LEGACY_AGENTS_FILE = "AGENTS.md";
    private const string MODULES_DIR = "modules";
    private const int MAX_SOUL_OPERATOR_SIZE = 20 * 1024;
    private const int MAX_MODULE_SIZE = 8 * 1024;

    private static readonly TypedKey<bool> _reloadKey = new("prompts.reloaded");

    private readonly IPubSub _pubSub;
    private readonly string _promptsDir;

    private PromptSnapshot _snapshot = PromptSnapshot.Empty;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private readonly Lock _debounceLock = new();

    public PromptLibrary(IPubSub pubSub)
        : this(pubSub, DEFAULT_PROMPTS_DIR)
    {
    }

    internal PromptLibrary(IPubSub pubSub, string promptsDir)
    {
        _pubSub = pubSub;
        _promptsDir = promptsDir;
    }

    public PromptSnapshot Snapshot
    {
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _snapshot);
    }

    public Task OnReadyAsync()
    {
        _ = _pubSub.Sub(_reloadKey, OnReloadPublishedAsync);
        SeedDefaultsIfMissing();
        RebuildSnapshot();
        StartWatcher();
        return Task.CompletedTask;
    }

    public string GetSoul()
        => Snapshot.Soul;

    public string GetOperatorDoc()
        => Snapshot.Operator;

    public IReadOnlyList<(string Name, string Content)> GetModules(
        IReadOnlyCollection<string>? enabled)
    {
        var snapshot = Snapshot;
        if (snapshot.Modules.Count == 0)
            return [];

        var results = new List<(string, string)>(snapshot.Modules.Count);

        // deterministic alpha order
        foreach (var (name, content) in snapshot.Modules.OrderBy(static kv => kv.Key, StringComparer.Ordinal))
        {
            if (enabled is { Count: > 0 }
                && !enabled.Contains(name))
                continue;

            results.Add((name, content));
        }

        return results;
    }

    /// <summary>
    /// Returns the content of a prompt file directly from disk.
    /// relativePath is relative to the prompts dir (e.g. "SOUL.md", "modules/foo.md").
    /// Returns (content, size) or (null, 0) if the file doesn't exist or path escapes the root.
    /// </summary>
    public (string? Content, int Size) ReadRaw(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_promptsDir, relativePath));
        var rootFull = Path.GetFullPath(_promptsDir);

        if (!fullPath.StartsWith(rootFull, StringComparison.Ordinal))
            return (null, 0);

        if (!File.Exists(fullPath))
            return (null, 0);

        try
        {
            var content = File.ReadAllText(fullPath);
            return (content, content.Length);
        }
        catch
        {
            return (null, 0);
        }
    }

    public IReadOnlyList<string> ListModules()
    {
        var snapshot = Snapshot;
        if (snapshot.Modules.Count == 0)
            return [];

        return snapshot.Modules.Keys
            .OrderBy(static k => k, StringComparer.Ordinal)
            .ToList();
    }

    public bool TryWrite(string relativePath, string content, out string error)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_promptsDir, relativePath));
        var rootFull = Path.GetFullPath(_promptsDir);

        if (!fullPath.StartsWith(rootFull, StringComparison.Ordinal))
        {
            error = "Path escapes the prompts directory.";
            return false;
        }

        // don't allow writes into examples/
        var rel = Path.GetRelativePath(rootFull, fullPath);
        if (rel.StartsWith("examples", StringComparison.OrdinalIgnoreCase))
        {
            error = "Cannot write to the examples directory.";
            return false;
        }

        var isModule = rel.StartsWith(MODULES_DIR, StringComparison.OrdinalIgnoreCase);
        var maxSize = isModule ? MAX_MODULE_SIZE : MAX_SOUL_OPERATOR_SIZE;

        if (content.Length > maxSize)
        {
            error = $"Content exceeds the {maxSize / 1024}KB limit.";
            return false;
        }

        if (!fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only .md files are allowed.";
            return false;
        }

        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            var tmpPath = fullPath + ".tmp";
            File.WriteAllText(tmpPath, content);
            File.Move(tmpPath, fullPath, overwrite: true);
        }
        catch (Exception ex)
        {
            error = $"Failed to write: {ex.Message}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public async Task ReloadAsync()
    {
        RebuildSnapshot();
        await _pubSub.Pub(_reloadKey, true);
    }

    private ValueTask OnReloadPublishedAsync(bool _)
    {
        RebuildSnapshot();
        return default;
    }

    private void RebuildSnapshot()
    {
        var soulPath = Path.Combine(_promptsDir, SOUL_FILE);
        var operatorPath = Path.Combine(_promptsDir, OPERATOR_FILE);
        var modulesPath = Path.Combine(_promptsDir, MODULES_DIR);

        var soul = ReadFileOrEmpty(soulPath);
        var operatorDoc = ReadFileOrEmpty(operatorPath);

        var modules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(modulesPath))
        {
            foreach (var file in Directory.EnumerateFiles(modulesPath, "*.md"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                // Include even empty modules so operators can discover them and toggle them via .apromptmodule.
                // Empty content is filtered at assembly time in SystemPromptBuilder.
                modules[name] = ReadFileOrEmpty(file);
            }
        }

        var snapshot = new PromptSnapshot(
            soul,
            operatorDoc,
            modules.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));

        Volatile.Write(ref _snapshot, snapshot);
        Log.Information("PromptLibrary: loaded SOUL ({SoulLen} chars), OPERATOR ({OperatorLen} chars), {ModuleCount} modules",
            soul.Length, operatorDoc.Length, modules.Count);
    }

    private static string ReadFileOrEmpty(string path)
    {
        if (!File.Exists(path))
            return string.Empty;

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "PromptLibrary: failed to read {Path}", path);
            return string.Empty;
        }
    }

    private void SeedDefaultsIfMissing()
    {
        // The defaults live in data/ai/prompts/ (copied from source tree by build).
        // On first boot with a fresh data dir, the build would have already placed them.
        // This method ensures the directory structure exists even if someone deleted it.
        Directory.CreateDirectory(Path.Combine(_promptsDir, MODULES_DIR));
        Directory.CreateDirectory(Path.Combine(_promptsDir, "examples"));

        // One-shot legacy rename for installs that ran a pre-release build with the old name.
        var legacyPath = Path.Combine(_promptsDir, LEGACY_AGENTS_FILE);
        var operatorPath = Path.Combine(_promptsDir, OPERATOR_FILE);
        if (File.Exists(legacyPath) && !File.Exists(operatorPath))
        {
            try
            {
                File.Move(legacyPath, operatorPath);
                Log.Information("PromptLibrary: migrated {Old} to {New}", LEGACY_AGENTS_FILE, OPERATOR_FILE);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PromptLibrary: failed to rename {Old} to {New}", LEGACY_AGENTS_FILE, OPERATOR_FILE);
            }
        }

        // Only operator-editable files are seeded. Tool usage guidance lives in code
        // (IAiTool.SystemGuidance) and platform guidance lives in DefaultPrompts.PlatformGuidance
        // so they always stay in sync with the bot's actual tools/behavior.
        // The modules/ directory is left empty by default - operators drop their own .md files
        // there for personas, specialities, or other composable flavor.
        SeedFileIfMissing(Path.Combine(_promptsDir, SOUL_FILE), DefaultPrompts.Soul);
        SeedFileIfMissing(operatorPath, DefaultPrompts.Operator);
    }

    private static void SeedFileIfMissing(string path, string content)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(fs);
            writer.Write(content);
            Log.Information("PromptLibrary: seeded default {Path}", path);
        }
        catch (IOException)
        {
            // file already exists - expected
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "PromptLibrary: failed to seed {Path}", path);
        }
    }

    private void StartWatcher()
    {
        var fullPath = Path.GetFullPath(_promptsDir);
        if (!Directory.Exists(fullPath))
            return;

        _watcher = new FileSystemWatcher(fullPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                           | NotifyFilters.LastWrite
                           | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += (_, _) => ScheduleRebuild();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!e.FullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return;

        // ignore examples/
        var rel = Path.GetRelativePath(Path.GetFullPath(_promptsDir), e.FullPath);
        if (rel.StartsWith("examples", StringComparison.OrdinalIgnoreCase))
            return;

        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(
                static state => ((PromptLibrary)state!).RebuildSnapshot(),
                this,
                TimeSpan.FromSeconds(1),
                Timeout.InfiniteTimeSpan);
        }
    }
}
