using System.Security.Cryptography;

namespace NadekoBot.Modules.Utility.AiAgent;

public sealed class SemanticIndex<TEntry>
{
    private readonly EmbeddingService _embedder;
    private readonly string _cachePath;
    private readonly Func<TEntry, string> _searchTextSelector;

    // One reference, so a search during a reload never pairs the new entries with the old embeddings.
    private sealed record Snapshot(TEntry[] Entries, float[][] Embeddings);

    private volatile Snapshot? _snapshot;
    private byte[]? _currentHash;

    public bool IsReady => _snapshot is not null;

    public SemanticIndex(
        EmbeddingService embedder,
        string cachePath,
        Func<TEntry, string> searchTextSelector)
    {
        _embedder = embedder;
        _cachePath = cachePath;
        _searchTextSelector = searchTextSelector;
    }

    public Task<BuildResult> BuildAsync(TEntry[] entries, byte[] contentHash)
    {
        if (!_embedder.IsReady)
        {
            Log.Warning("SemanticIndex: Embedding service not ready, cannot build index");
            return Task.FromResult(new BuildResult(entries.Length, 0, false, Skipped: true));
        }

        if (_currentHash is not null && contentHash.AsSpan().SequenceEqual(_currentHash))
            return Task.FromResult(new BuildResult(entries.Length, 0, false, Skipped: true));

        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (TryLoadCachedEmbeddingsInternal(contentHash, entries.Length, out var cached))
        {
            _snapshot = new(entries, cached);
            _currentHash = contentHash;
            sw.Stop();
            return Task.FromResult(new BuildResult(entries.Length, sw.ElapsedMilliseconds, FromCache: true, Skipped: false));
        }

        sw.Restart();

        var embeddings = new float[entries.Length][];
        for (var i = 0; i < entries.Length; i++)
            embeddings[i] = _embedder.Embed(_searchTextSelector(entries[i]));

        SaveCachedEmbeddingsInternal(contentHash, embeddings);

        _snapshot = new(entries, embeddings);
        _currentHash = contentHash;

        sw.Stop();
        return Task.FromResult(new BuildResult(entries.Length, sw.ElapsedMilliseconds, FromCache: false, Skipped: false));
    }

    public (TEntry Entry, float Score)[] Search(string query, int topK = 5)
    {
        if (_snapshot is not { } snap)
            return [];

        var queryEmb = _embedder.Embed(query);
        var scores = new (float Score, int Index)[snap.Entries.Length];

        for (var i = 0; i < snap.Entries.Length; i++)
            scores[i] = (CosineSimilarity(queryEmb, snap.Embeddings[i]), i);

        Array.Sort(scores, static (a, b) => b.Score.CompareTo(a.Score));

        var resultCount = Math.Min(topK, scores.Length);
        var results = new (TEntry Entry, float Score)[resultCount];
        for (var i = 0; i < resultCount; i++)
        {
            var (score, idx) = scores[i];
            results[i] = (snap.Entries[idx], score);
        }

        return results;
    }

    public (TEntry[] Entries, float[] Scores)? ScoreAll(string query)
    {
        if (_snapshot is not { } snap)
            return null;

        var (entries, embeddings) = snap;
        var queryEmb = _embedder.Embed(query);
        var scores = new float[entries.Length];
        for (var i = 0; i < entries.Length; i++)
            scores[i] = CosineSimilarity(queryEmb, embeddings[i]);

        return (entries, scores);
    }

    private bool TryLoadCachedEmbeddingsInternal(byte[] expectedHash, int entryCount, out float[][] embeddings)
    {
        embeddings = [];

        if (!File.Exists(_cachePath))
            return false;

        try
        {
            using var fs = File.OpenRead(_cachePath);
            using var reader = new BinaryReader(fs);

            var storedHash = reader.ReadBytes(32);
            if (!storedHash.AsSpan().SequenceEqual(expectedHash))
                return false;

            var count = reader.ReadInt32();
            if (count != entryCount)
                return false;

            var result = new float[count][];
            for (var i = 0; i < count; i++)
            {
                var vec = new float[EmbeddingService.EMBEDDING_DIM];
                for (var j = 0; j < EmbeddingService.EMBEDDING_DIM; j++)
                    vec[j] = reader.ReadSingle();
                result[i] = vec;
            }

            embeddings = result;
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SemanticIndex: Failed to load cache from {Path}", _cachePath);
            return false;
        }
    }

    private void SaveCachedEmbeddingsInternal(byte[] hash, float[][] embeddings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);

            using var fs = File.Create(_cachePath);
            using var writer = new BinaryWriter(fs);

            writer.Write(hash);
            writer.Write(embeddings.Length);

            for (var i = 0; i < embeddings.Length; i++)
                for (var j = 0; j < EmbeddingService.EMBEDDING_DIM; j++)
                    writer.Write(embeddings[i][j]);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SemanticIndex: Failed to save cache to {Path}", _cachePath);
        }
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0f;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot;
    }
}

public readonly record struct BuildResult(int Count, long ElapsedMs, bool FromCache, bool Skipped);
