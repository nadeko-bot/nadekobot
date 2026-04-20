using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace NadekoBot.Modules.Utility.AiAgent;

public sealed class EmbeddingService(IHttpClientFactory httpFactory) : INService
{
    private const string MODEL_DIR = "data/ai/models/all-MiniLM-L6-v2";
    private const string MODEL_FILE = "model.onnx";
    private const string VOCAB_FILE = "vocab.txt";
    public const int EMBEDDING_DIM = 384;
    private const int MAX_SEQ_LEN = 128;

    private static readonly string[] _modelUrls =
    [
        "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/onnx/model_quantized.onnx",
    ];

    private static readonly string _vocabUrl =
        "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/vocab.txt";

    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private volatile bool _ready;

    public bool IsReady => _ready;

    public async Task EnsureModelReadyAsync()
    {
        if (_ready)
            return;

        await EnsureModelDownloadedInternalAsync();

        var modelPath = Path.Combine(MODEL_DIR, MODEL_FILE);
        var vocabPath = Path.Combine(MODEL_DIR, VOCAB_FILE);

        if (!File.Exists(modelPath) || !File.Exists(vocabPath))
        {
            Log.Warning("EmbeddingService: Model files missing after download attempt");
            return;
        }

        EnsureOnnxLoadedInternal(modelPath, vocabPath);
        _ready = true;
    }

    public float[] Embed(string text)
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

    private void EnsureOnnxLoadedInternal(string modelPath, string vocabPath)
    {
        if (_session is not null)
            return;

        var sessionOptions = new SessionOptions();
        sessionOptions.InterOpNumThreads = 1;
        sessionOptions.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
        _session = new InferenceSession(modelPath, sessionOptions);
        _tokenizer = BertTokenizer.Create(vocabPath);
    }

    private async Task EnsureModelDownloadedInternalAsync()
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
