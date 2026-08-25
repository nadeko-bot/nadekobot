using System.Security.Cryptography;
using System.Text;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Utility.AiAgent;

public sealed record DataToolEntry(string Name, string Description, string SearchText);

public sealed class DataToolSearchService(
    EmbeddingService embedder,
    Lazy<IAiToolRegistry> toolRegistry) : INService, IReadyExecutor
{
    private const string CACHE_PATH = "data/ai/embeddings/datatools.cache";

    private readonly SemanticIndex<DataToolEntry> _index = new(
        embedder,
        CACHE_PATH,
        static e => e.SearchText);

    public bool IsReady => _index.IsReady;

    public Task OnReadyAsync()
    {
        _ = Task.Run(BuildIndexInternalAsync);
        return Task.CompletedTask;
    }

    private async Task BuildIndexInternalAsync()
    {
        try
        {
            await embedder.EnsureModelReadyAsync();

            var dataTools = toolRegistry.Value.GetDataTools();
            if (dataTools.Count == 0)
            {
                Log.Information("DataToolSearch: No data tools registered, skipping index");
                return;
            }

            var entries = new DataToolEntry[dataTools.Count];
            var i = 0;
            foreach (var (name, tool) in dataTools)
            {
                entries[i++] = new DataToolEntry(
                    name,
                    tool.Description,
                    $"{name} | {tool.Description}");
            }

            var hashInput = new StringBuilder();
            foreach (var entry in entries.OrderBy(static e => e.Name, StringComparer.Ordinal))
                hashInput.Append(entry.Name).Append('|').Append(entry.Description).Append('\n');

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput.ToString()));

            var result = await _index.BuildAsync(entries, hash);

            if (result.Skipped)
                return;

            if (result.FromCache)
                Log.Information("DataToolSearch: Loaded {Count} tools from cache in {Elapsed}ms", result.Count, result.ElapsedMs);
            else
                Log.Information("DataToolSearch: Indexed {Count} tools in {Elapsed}ms", result.Count, result.ElapsedMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DataToolSearch: Failed to build index");
        }
    }

    public (DataToolEntry Entry, float Score)[] Search(string query, int topK = 5)
        => _index.Search(query, topK);
}
