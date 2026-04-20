using System.Security.Cryptography;
using System.Text.Json;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Utility.AiAgent;

public sealed class CommandSearchService(
    EmbeddingService embedder,
    ShardData shardData,
    IPubSub pubSub) : INService, IReadyExecutor
{
    private const string COMMAND_LIST_PATH = "data/commandlist.json";
    private const string INTENT_HEAD_PATH = "data/ai/intent-head.bin";
    private const string EMBEDDINGS_CACHE_PATH = "data/ai/embeddings/commands.cache";
    private const string LEGACY_CACHE_PATH = "data/ai/command-embeddings.cache";
    private const int EMBEDDING_DIM = EmbeddingService.EMBEDDING_DIM;
    private const int HIDDEN_DIM = 128;
    private const int NUM_CLASSES = 2;
    private const float BN_EPS = 1e-5f;

    private static readonly TypedKey<bool> _reloadKey = new("cmdsearch.reload");

    private readonly SemanticIndex<CommandEntry> _index = new(
        embedder,
        EMBEDDINGS_CACHE_PATH,
        static e => e.SearchText);

    // Classification head weights
    private float[]? _w1;
    private float[]? _b1;
    private float[]? _bnGamma;
    private float[]? _bnBeta;
    private float[]? _bnMean;
    private float[]? _bnVar;
    private float[]? _w2;
    private float[]? _b2;
    private bool _headReady;
    private byte[]? _currentHash;

    public bool IsReady => _index.IsReady;

    public Task OnReadyAsync()
    {
        _ = pubSub.Sub(_reloadKey, OnReloadRequestedInternalAsync);
        _ = Task.Run(InitializeInternalAsync);

        if (shardData.ShardId == 0)
            _ = Task.Run(WatchForChangesInternalAsync);

        return Task.CompletedTask;
    }

    private async Task InitializeInternalAsync()
    {
        try
        {
            Log.Information("CommandSearch: Waiting for commandlist.json...");

            for (var i = 0; i < 24; i++)
            {
                if (File.Exists(COMMAND_LIST_PATH))
                    break;
                await Task.Delay(5_000);
            }

            if (!File.Exists(COMMAND_LIST_PATH))
            {
                Log.Warning("CommandSearch: Command list not found at {Path}", COMMAND_LIST_PATH);
                return;
            }

            MigrateLegacyCacheInternal();
            await embedder.EnsureModelReadyAsync();
            await LoadAndBuildIndexInternalAsync();
            LoadClassificationHeadInternal();

            Log.Information("CommandSearch: Ready, head={HeadReady}", _headReady);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CommandSearch: Failed to initialize");
        }
    }

    private async Task WatchForChangesInternalAsync()
    {
        await Task.Delay(1_000);
        await TryReloadInternalAsync();

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync())
        {
            await TryReloadInternalAsync();
        }
    }

    private async Task TryReloadInternalAsync()
    {
        try
        {
            if (!File.Exists(COMMAND_LIST_PATH))
                return;

            var cmdListBytes = await File.ReadAllBytesAsync(COMMAND_LIST_PATH);
            var cmdListHash = SHA256.HashData(cmdListBytes);

            if (_currentHash is not null && cmdListHash.AsSpan().SequenceEqual(_currentHash))
                return;

            Log.Information("CommandSearch: Detected commandlist.json change, reloading...");
            await embedder.EnsureModelReadyAsync();
            await LoadAndBuildIndexInternalAsync();

            if (!_headReady)
                LoadClassificationHeadInternal();

            await pubSub.Pub(_reloadKey, true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CommandSearch: Error watching for commandlist.json changes");
        }
    }

    private async ValueTask OnReloadRequestedInternalAsync(bool _)
    {
        try
        {
            if (!File.Exists(COMMAND_LIST_PATH))
                return;

            var cmdListBytes = await File.ReadAllBytesAsync(COMMAND_LIST_PATH);
            var cmdListHash = SHA256.HashData(cmdListBytes);

            if (_currentHash is not null && cmdListHash.AsSpan().SequenceEqual(_currentHash))
                return;

            Log.Information("CommandSearch: Reload notification received, reloading...");
            await embedder.EnsureModelReadyAsync();
            await LoadAndBuildIndexInternalAsync();

            if (!_headReady)
                LoadClassificationHeadInternal();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CommandSearch: Error reloading on notification");
        }
    }

    private async Task LoadAndBuildIndexInternalAsync()
    {
        var cmdListBytes = await File.ReadAllBytesAsync(COMMAND_LIST_PATH);
        var cmdListHash = SHA256.HashData(cmdListBytes);

        var commands = LoadCommandListInternal(cmdListBytes);
        if (commands.Length == 0)
        {
            Log.Warning("CommandSearch: No commands found in commandlist.json");
            return;
        }

        await _index.BuildAsync(commands, cmdListHash);
        _currentHash = cmdListHash;

        Log.Information("CommandSearch: Indexed {Count} commands", commands.Length);
    }

    public CommandSearchResult[] Search(string query, int topK = 5)
    {
        var results = _index.Search(query, topK);
        var output = new CommandSearchResult[results.Length];
        for (var i = 0; i < results.Length; i++)
            output[i] = new(results[i].Entry, results[i].Score);
        return output;
    }

    public bool IsCommandIntent(string normalizedText)
    {
        if (!_index.IsReady || !_headReady)
            return false;

        var emb = embedder.Embed(normalizedText);
        return ClassifyIntentInternal(emb);
    }

    private bool ClassifyIntentInternal(float[] embedding)
    {
        var hidden = new float[HIDDEN_DIM];
        for (var j = 0; j < HIDDEN_DIM; j++)
        {
            var sum = _b1![j];
            var wOffset = j * EMBEDDING_DIM;
            for (var k = 0; k < EMBEDDING_DIM; k++)
                sum += embedding[k] * _w1![wOffset + k];
            hidden[j] = sum;
        }

        for (var j = 0; j < HIDDEN_DIM; j++)
            hidden[j] = (hidden[j] - _bnMean![j]) / MathF.Sqrt(_bnVar![j] + BN_EPS) * _bnGamma![j] + _bnBeta![j];

        for (var j = 0; j < HIDDEN_DIM; j++)
            hidden[j] = MathF.Max(0, hidden[j]);

        var output = new float[NUM_CLASSES];
        for (var j = 0; j < NUM_CLASSES; j++)
        {
            var sum = _b2![j];
            var wOffset = j * HIDDEN_DIM;
            for (var k = 0; k < HIDDEN_DIM; k++)
                sum += hidden[k] * _w2![wOffset + k];
            output[j] = sum;
        }

        return output[1] > output[0];
    }

    private void LoadClassificationHeadInternal()
    {
        if (!File.Exists(INTENT_HEAD_PATH))
        {
            Log.Warning("CommandSearch: Intent head not found at {Path}", INTENT_HEAD_PATH);
            return;
        }

        try
        {
            using var fs = File.OpenRead(INTENT_HEAD_PATH);
            using var reader = new BinaryReader(fs);

            _w1 = ReadFloatsInternal(reader, HIDDEN_DIM * EMBEDDING_DIM);
            _b1 = ReadFloatsInternal(reader, HIDDEN_DIM);
            _bnGamma = ReadFloatsInternal(reader, HIDDEN_DIM);
            _bnBeta = ReadFloatsInternal(reader, HIDDEN_DIM);
            _bnMean = ReadFloatsInternal(reader, HIDDEN_DIM);
            _bnVar = ReadFloatsInternal(reader, HIDDEN_DIM);
            _w2 = ReadFloatsInternal(reader, NUM_CLASSES * HIDDEN_DIM);
            _b2 = ReadFloatsInternal(reader, NUM_CLASSES);

            _headReady = true;
            Log.Information("CommandSearch: Loaded intent classification head ({Size}KB)", fs.Length / 1024);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CommandSearch: Failed to load intent classification head");
        }
    }

    private static float[] ReadFloatsInternal(BinaryReader reader, int count)
    {
        var arr = new float[count];
        for (var i = 0; i < count; i++)
            arr[i] = reader.ReadSingle();
        return arr;
    }

    private static void MigrateLegacyCacheInternal()
    {
        if (File.Exists(LEGACY_CACHE_PATH) && !File.Exists(EMBEDDINGS_CACHE_PATH))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(EMBEDDINGS_CACHE_PATH)!);
                File.Move(LEGACY_CACHE_PATH, EMBEDDINGS_CACHE_PATH);
                Log.Information("CommandSearch: Migrated cache from {Old} to {New}", LEGACY_CACHE_PATH, EMBEDDINGS_CACHE_PATH);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CommandSearch: Failed to migrate legacy cache");
            }
        }
    }

    private static CommandEntry[] LoadCommandListInternal(byte[] jsonBytes)
    {
        using var doc = JsonDocument.Parse(jsonBytes);

        var entries = new List<CommandEntry>();
        foreach (var module in doc.RootElement.EnumerateObject())
        {
            foreach (var cmd in module.Value.EnumerateArray())
            {
                var aliases = cmd.TryGetProperty("Aliases", out var aliasEl)
                    ? aliasEl.EnumerateArray().Select(static a => a.GetString() ?? "").ToArray()
                    : [];

                var desc = cmd.TryGetProperty("Description", out var descEl)
                    ? descEl.GetString() ?? ""
                    : "";

                var usage = cmd.TryGetProperty("Usage", out var usageEl)
                    ? usageEl.EnumerateArray().Select(static u => u.GetString() ?? "").ToArray()
                    : [];

                var submodule = cmd.TryGetProperty("Submodule", out var subEl)
                    ? subEl.GetString() ?? ""
                    : "";

                var moduleName = module.Name;

                var requirements = cmd.TryGetProperty("Requirements", out var reqEl)
                    ? reqEl.EnumerateArray().Select(static r => r.GetString() ?? "").ToArray()
                    : [];

                if (aliases.Length == 0)
                    continue;

                var searchText = $"{string.Join(" ", aliases)} | {moduleName}/{submodule} | {desc}";

                entries.Add(new CommandEntry(
                    aliases,
                    desc,
                    usage,
                    moduleName,
                    submodule,
                    requirements,
                    searchText));
            }
        }

        return entries.ToArray();
    }
}

public sealed record CommandEntry(
    string[] Aliases,
    string Description,
    string[] Usage,
    string Module,
    string Submodule,
    string[] Requirements,
    string SearchText);

public sealed record CommandSearchResult(CommandEntry Command, float Score);
