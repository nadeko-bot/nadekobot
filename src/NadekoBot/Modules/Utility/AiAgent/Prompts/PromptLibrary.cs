using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Utility.AiAgent.Prompts;

public sealed class PromptLibrary : INService, IReadyExecutor
{
    public const string DEFAULT_PROMPTS_DIR = "data/ai/prompts";
    private const string SOUL_FILE = "SOUL.md";
    private const string OPERATOR_FILE = "OPERATOR.md";
    private const int MAX_SIZE = 20 * 1024;

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

    public string Read(PromptKind kind)
        => kind == PromptKind.Soul ? Snapshot.Soul : Snapshot.Operator;

    public bool TryWrite(PromptKind kind, string content, out string error)
    {
        if (content.Length > MAX_SIZE)
        {
            error = $"Content exceeds the {MAX_SIZE / 1024}KB limit.";
            return false;
        }

        var fileName = kind == PromptKind.Soul ? SOUL_FILE : OPERATOR_FILE;
        var fullPath = Path.GetFullPath(Path.Combine(_promptsDir, fileName));

        try
        {
            Directory.CreateDirectory(_promptsDir);
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
        var soul = ReadFileOrEmpty(Path.Combine(_promptsDir, SOUL_FILE));
        var operatorDoc = ReadFileOrEmpty(Path.Combine(_promptsDir, OPERATOR_FILE));

        Volatile.Write(ref _snapshot, new PromptSnapshot(soul, operatorDoc));
        Log.Information(
            "PromptLibrary: loaded SOUL ({SoulLen} chars), OPERATOR ({OperatorLen} chars)",
            soul.Length,
            operatorDoc.Length);
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
        Directory.CreateDirectory(_promptsDir);
        SeedFileIfMissing(Path.Combine(_promptsDir, SOUL_FILE), DefaultPrompts.Soul);
        SeedFileIfMissing(Path.Combine(_promptsDir, OPERATOR_FILE), DefaultPrompts.Operator);
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
            IncludeSubdirectories = false,
            Filter = "*.md",
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
        var name = Path.GetFileName(e.FullPath);
        if (!name.Equals(SOUL_FILE, StringComparison.OrdinalIgnoreCase)
            && !name.Equals(OPERATOR_FILE, StringComparison.OrdinalIgnoreCase))
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
