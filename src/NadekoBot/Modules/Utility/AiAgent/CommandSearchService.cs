using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Provides semantic search over bot commands using an ONNX embedding model (all-MiniLM-L6-v2),
/// and intent classification using a trained classification head (196KB binary weights).
/// Downloads the embedding model on first use and builds an in-memory vector index.
/// </summary>
public sealed class CommandSearchService(
    IHttpClientFactory httpFactory) : INService, IReadyExecutor
{
    private const string MODEL_DIR = "data/ai/models/all-MiniLM-L6-v2";
    private const string MODEL_FILE = "model.onnx";
    private const string VOCAB_FILE = "vocab.txt";
    private const string COMMAND_LIST_PATH = "data/commandlist.json";
    private const string INTENT_HEAD_PATH = "data/ai/intent-head.bin";
    private const int EMBEDDING_DIM = 384;
    private const int HIDDEN_DIM = 128;
    private const int NUM_CLASSES = 2;
    private const int MAX_SEQ_LEN = 128;
    private const float BN_EPS = 1e-5f;

    private static readonly string[] _modelUrls =
    [
        "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/onnx/model_quantized.onnx",
    ];

    private static readonly string _vocabUrl =
        "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/vocab.txt";

    private float[][]? _embeddings;
    private CommandEntry[]? _commands;
    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private volatile bool _ready;

    // Classification head weights (loaded from intent-head.bin)
    private float[]? _w1;       // [HIDDEN_DIM * EMBEDDING_DIM] row-major
    private float[]? _b1;       // [HIDDEN_DIM]
    private float[]? _bnGamma;  // [HIDDEN_DIM]
    private float[]? _bnBeta;   // [HIDDEN_DIM]
    private float[]? _bnMean;   // [HIDDEN_DIM]
    private float[]? _bnVar;    // [HIDDEN_DIM]
    private float[]? _w2;       // [NUM_CLASSES * HIDDEN_DIM] row-major
    private float[]? _b2;       // [NUM_CLASSES]
    private bool _headReady;

    /// <summary>
    /// Whether the search index and intent classifier are ready for queries
    /// </summary>
    public bool IsReady => _ready;

    public Task OnReadyAsync()
    {
        _ = Task.Run(InitializeAsync);
        return Task.CompletedTask;
    }

    private async Task InitializeAsync()
    {
        try
        {
            Log.Information("CommandSearch: Waiting for commandlist.json...");

            for (var i = 0; i < 30; i++)
            {
                if (File.Exists(COMMAND_LIST_PATH))
                    break;
                await Task.Delay(2000);
            }

            if (!File.Exists(COMMAND_LIST_PATH))
            {
                Log.Warning("CommandSearch: Command list not found at {Path}, semantic search disabled", COMMAND_LIST_PATH);
                return;
            }

            Log.Information("CommandSearch: Loading command list...");
            var commands = await LoadCommandListAsync();
            if (commands.Length == 0)
            {
                Log.Warning("CommandSearch: No commands found, semantic search disabled");
                return;
            }

            Log.Information("CommandSearch: Loaded {Count} commands, ensuring model is downloaded...", commands.Length);
            await EnsureModelDownloadedAsync();

            var modelPath = Path.Combine(MODEL_DIR, MODEL_FILE);
            var vocabPath = Path.Combine(MODEL_DIR, VOCAB_FILE);

            if (!File.Exists(modelPath) || !File.Exists(vocabPath))
            {
                Log.Warning("CommandSearch: Model files missing after download, semantic search disabled");
                return;
            }

            Log.Information("CommandSearch: Loading ONNX model and tokenizer...");
            _session = new InferenceSession(modelPath);
            _tokenizer = BertTokenizer.Create(vocabPath);

            Log.Information("CommandSearch: Embedding {Count} commands...", commands.Length);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var embeddings = new float[commands.Length][];
            for (var i = 0; i < commands.Length; i++)
                embeddings[i] = Embed(commands[i].SearchText);

            sw.Stop();

            _commands = commands;
            _embeddings = embeddings;

            LoadClassificationHead();

            _ready = true;

            Log.Information("CommandSearch: Index ready - {Count} commands embedded in {Elapsed}ms, head={HeadReady}",
                commands.Length, sw.ElapsedMilliseconds, _headReady);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CommandSearch: Failed to initialize");
        }
    }

    /// <summary>
    /// Loads the trained classification head weights from a binary file.
    /// Format: W1[128*384] b1[128] bnGamma[128] bnBeta[128] bnMean[128] bnVar[128] W2[2*128] b2[2]
    /// </summary>
    private void LoadClassificationHead()
    {
        if (!File.Exists(INTENT_HEAD_PATH))
        {
            Log.Warning("CommandSearch: Intent head not found at {Path}, intent classification disabled", INTENT_HEAD_PATH);
            return;
        }

        try
        {
            using var fs = File.OpenRead(INTENT_HEAD_PATH);
            using var reader = new BinaryReader(fs);

            _w1 = ReadFloats(reader, HIDDEN_DIM * EMBEDDING_DIM);
            _b1 = ReadFloats(reader, HIDDEN_DIM);
            _bnGamma = ReadFloats(reader, HIDDEN_DIM);
            _bnBeta = ReadFloats(reader, HIDDEN_DIM);
            _bnMean = ReadFloats(reader, HIDDEN_DIM);
            _bnVar = ReadFloats(reader, HIDDEN_DIM);
            _w2 = ReadFloats(reader, NUM_CLASSES * HIDDEN_DIM);
            _b2 = ReadFloats(reader, NUM_CLASSES);

            _headReady = true;
            var sizeKb = fs.Length / 1024;
            Log.Information("CommandSearch: Loaded intent classification head ({Size}KB)", sizeKb);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CommandSearch: Failed to load intent classification head");
        }
    }

    private static float[] ReadFloats(BinaryReader reader, int count)
    {
        var arr = new float[count];
        for (var i = 0; i < count; i++)
            arr[i] = reader.ReadSingle();
        return arr;
    }

    /// <summary>
    /// Runs the classification head forward pass on an embedding vector.
    /// Architecture: Linear(384,128) -> BatchNorm -> ReLU -> Linear(128,2)
    /// Returns true if the input is classified as a command/trigger (class 1).
    /// </summary>
    private bool ClassifyIntent(float[] embedding)
    {
        // Layer 1: Linear(384, 128)
        var hidden = new float[HIDDEN_DIM];
        for (var j = 0; j < HIDDEN_DIM; j++)
        {
            var sum = _b1![j];
            var wOffset = j * EMBEDDING_DIM;
            for (var k = 0; k < EMBEDDING_DIM; k++)
                sum += embedding[k] * _w1![wOffset + k];
            hidden[j] = sum;
        }

        // BatchNorm: (x - mean) / sqrt(var + eps) * gamma + beta
        for (var j = 0; j < HIDDEN_DIM; j++)
            hidden[j] = (hidden[j] - _bnMean![j]) / MathF.Sqrt(_bnVar![j] + BN_EPS) * _bnGamma![j] + _bnBeta![j];

        // ReLU
        for (var j = 0; j < HIDDEN_DIM; j++)
            hidden[j] = MathF.Max(0, hidden[j]);

        // Layer 2: Linear(128, 2)
        var output = new float[NUM_CLASSES];
        for (var j = 0; j < NUM_CLASSES; j++)
        {
            var sum = _b2![j];
            var wOffset = j * HIDDEN_DIM;
            for (var k = 0; k < HIDDEN_DIM; k++)
                sum += hidden[k] * _w2![wOffset + k];
            output[j] = sum;
        }

        // Class 1 = command/trigger, Class 0 = not
        return output[1] > output[0];
    }

    /// <summary>
    /// Search commands by semantic similarity to the query
    /// </summary>
    public CommandSearchResult[] Search(string query, int topK = 5)
    {
        if (!_ready || _embeddings is null || _commands is null)
            return [];

        var queryEmb = Embed(query);
        var scores = new (float Score, int Index)[_commands.Length];

        for (var i = 0; i < _commands.Length; i++)
            scores[i] = (CosineSimilarity(queryEmb, _embeddings[i]), i);

        Array.Sort(scores, (a, b) => b.Score.CompareTo(a.Score));

        var results = new CommandSearchResult[Math.Min(topK, scores.Length)];
        for (var i = 0; i < results.Length; i++)
        {
            var (score, idx) = scores[i];
            var cmd = _commands[idx];
            results[i] = new(cmd, score);
        }

        return results;
    }

    /// <summary>
    /// Classify whether a message containing the bot's name is a command (addressing the bot)
    /// or casual (talking about the bot). Returns true if it's likely a command.
    /// </summary>
    public bool IsCommandIntent(string normalizedText)
    {
        if (!_ready || !_headReady)
            return false;

        var emb = Embed(normalizedText);
        var result = ClassifyIntent(emb);

        return result;
    }

    /// <summary>
    /// Embed a text string into a 384-dim float vector using mean pooling
    /// </summary>
    internal float[] Embed(string text)
    {
        var ids = _tokenizer!.EncodeToIds(text, MAX_SEQ_LEN, out _, out _);
        var inputIds = new int[ids.Count];
        var attentionMask = new int[ids.Count];
        var tokenTypeIds = new int[ids.Count];

        for (var i = 0; i < ids.Count; i++)
        {
            inputIds[i] = ids[i];
            attentionMask[i] = 1;
            tokenTypeIds[i] = 0;
        }

        var shape = new[] { 1, ids.Count };

        var longInputIds = new long[ids.Count];
        var longAttentionMask = new long[ids.Count];
        var longTokenTypeIds = new long[ids.Count];

        for (var i = 0; i < ids.Count; i++)
        {
            longInputIds[i] = inputIds[i];
            longAttentionMask[i] = attentionMask[i];
            longTokenTypeIds[i] = tokenTypeIds[i];
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids",
                new DenseTensor<long>(longInputIds, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask",
                new DenseTensor<long>(longAttentionMask, shape)),
            NamedOnnxValue.CreateFromTensor("token_type_ids",
                new DenseTensor<long>(longTokenTypeIds, shape)),
        };

        using var results = _session!.Run(inputs);
        var output = results.First().AsTensor<float>();

        var embedding = new float[EMBEDDING_DIM];
        var tokenCount = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            if (attentionMask[i] == 0)
                continue;

            for (var j = 0; j < EMBEDDING_DIM; j++)
                embedding[j] += output[0, i, j];
            tokenCount++;
        }

        if (tokenCount > 0)
        {
            for (var j = 0; j < EMBEDDING_DIM; j++)
                embedding[j] /= tokenCount;
        }

        // L2 normalize
        var norm = 0f;
        for (var j = 0; j < EMBEDDING_DIM; j++)
            norm += embedding[j] * embedding[j];
        norm = MathF.Sqrt(norm);

        if (norm > 0)
        {
            for (var j = 0; j < EMBEDDING_DIM; j++)
                embedding[j] /= norm;
        }

        return embedding;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0f;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot;
    }

    private async Task<CommandEntry[]> LoadCommandListAsync()
    {
        var json = await File.ReadAllTextAsync(COMMAND_LIST_PATH);
        using var doc = JsonDocument.Parse(json);

        var entries = new List<CommandEntry>();
        foreach (var module in doc.RootElement.EnumerateObject())
        {
            foreach (var cmd in module.Value.EnumerateArray())
            {
                var aliases = cmd.TryGetProperty("Aliases", out var aliasEl)
                    ? aliasEl.EnumerateArray().Select(a => a.GetString() ?? "").ToArray()
                    : [];

                var desc = cmd.TryGetProperty("Description", out var descEl)
                    ? descEl.GetString() ?? ""
                    : "";

                var usage = cmd.TryGetProperty("Usage", out var usageEl)
                    ? usageEl.EnumerateArray().Select(u => u.GetString() ?? "").ToArray()
                    : [];

                var submodule = cmd.TryGetProperty("Submodule", out var subEl)
                    ? subEl.GetString() ?? ""
                    : "";

                var moduleName = module.Name;

                var requirements = cmd.TryGetProperty("Requirements", out var reqEl)
                    ? reqEl.EnumerateArray().Select(r => r.GetString() ?? "").ToArray()
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

    private async Task EnsureModelDownloadedAsync()
    {
        Directory.CreateDirectory(MODEL_DIR);

        var modelPath = Path.Combine(MODEL_DIR, MODEL_FILE);
        var vocabPath = Path.Combine(MODEL_DIR, VOCAB_FILE);

        using var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);

        if (!File.Exists(modelPath))
        {
            var url = _modelUrls[0];
            Log.Information("Downloading embedding model from {Url}...", url);
            var bytes = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(modelPath, bytes);
            Log.Information("Embedding model downloaded ({Size}MB)", bytes.Length / 1024 / 1024);
        }

        if (!File.Exists(vocabPath))
        {
            Log.Information("Downloading vocab file...");
            var bytes = await http.GetByteArrayAsync(_vocabUrl);
            await File.WriteAllBytesAsync(vocabPath, bytes);
            Log.Information("Vocab file downloaded");
        }
    }
}

/// <summary>
/// A command entry in the search index
/// </summary>
public sealed record CommandEntry(
    string[] Aliases,
    string Description,
    string[] Usage,
    string Module,
    string Submodule,
    string[] Requirements,
    string SearchText);

/// <summary>
/// A search result with similarity score
/// </summary>
public sealed record CommandSearchResult(CommandEntry Command, float Score);
