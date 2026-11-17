using System.Collections.Frozen;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Help;

namespace NadekoBot.Modules.Utility.AiAgent;

public sealed class CommandSearchService(
    EmbeddingService embedder,
    ShardData shardData,
    IPubSub pubSub) : INService, IReadyExecutor
{
    private const string COMMAND_LIST_PATH = "data/commandlist.json";
    private const string EMBEDDINGS_CACHE_PATH = "data/ai/embeddings/commands.cache";
    private const string LEGACY_CACHE_PATH = "data/ai/command-embeddings.cache";
    private const float ALIAS_BOOST = 0.12f;

    private const float WORD_BOOST = 0.08f;

    private const int MAX_EDIT_NAME_LENGTH = 32;

    // A word which more commands hold tells them apart no better than "the", in any language.
    private const double MAX_WORD_SHARE = 0.2;

    // A request is a sentence; a longer text adds no better match.
    private const int MAX_QUERY_WORDS = 32;

    // A short alias such as "at", "la" or "se" is a common word in some language.
    private const int MIN_WORD_LENGTH = 3;

    public static readonly TypedKey<bool> ReloadKey = new("cmdsearch.reload");

    // Bumped when the parsing changes but the file does not, to invalidate the cache once.
    private static readonly byte[] _indexSchemaSalt = "v4-params"u8.ToArray();

    private static readonly Comparer<float> _descending = Comparer<float>.Create(static (a, b) => b.CompareTo(a));

    private readonly SemanticIndex<CommandEntry> _index = new(
        embedder,
        EMBEDDINGS_CACHE_PATH,
        static e => e.SearchText);

    private volatile FrozenDictionary<string, CommandEntry>? _byAlias;

    private volatile SearchWords? _searchWords;

    // One build at a time, so the index, the alias map and the word counts come from the same list.
    private readonly SemaphoreSlim _buildGate = new(1, 1);

    private byte[]? _currentHash;

    public bool IsReady => _index.IsReady;

    public Task OnReadyAsync()
    {
        _ = pubSub.Sub(ReloadKey, OnReloadRequestedInternalAsync);
        _ = Task.Run(InitializeInternalAsync);

        if (shardData.ShardId == 0)
            _ = Task.Run(WatchForChangesInternalAsync);

        return Task.CompletedTask;
    }

    private async Task InitializeInternalAsync()
    {
        try
        {
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
            var cmdListHash = ComputeListHashInternal(cmdListBytes);

            if (_currentHash is not null && cmdListHash.AsSpan().SequenceEqual(_currentHash))
                return;

            await embedder.EnsureModelReadyAsync();
            await LoadAndBuildIndexInternalAsync();
            await pubSub.Pub(ReloadKey, true);
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
            var cmdListHash = ComputeListHashInternal(cmdListBytes);

            if (_currentHash is not null && cmdListHash.AsSpan().SequenceEqual(_currentHash))
                return;

            await embedder.EnsureModelReadyAsync();
            await LoadAndBuildIndexInternalAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CommandSearch: Error reloading on notification");
        }
    }

    private async Task LoadAndBuildIndexInternalAsync()
        => await IndexCommandListAsync(await File.ReadAllBytesAsync(COMMAND_LIST_PATH));

    public async Task IndexCommandListAsync(byte[] cmdListBytes)
    {
        var cmdListHash = ComputeListHashInternal(cmdListBytes);

        var commands = ParseCommandList(cmdListBytes);
        if (commands.Length == 0)
        {
            Log.Warning("CommandSearch: No commands found in commandlist.json");
            return;
        }

        // Remembering the hash of a list which was never indexed would make every later reload skip it.
        if (!embedder.IsReady)
        {
            Log.Warning("CommandSearch: Embedding model is not ready, the command list will be indexed on the next reload");
            return;
        }

        await _buildGate.WaitAsync();
        try
        {
            if (_currentHash is not null && cmdListHash.AsSpan().SequenceEqual(_currentHash))
                return;

            var result = await _index.BuildAsync(commands, cmdListHash);
            if (result.Skipped)
                return;

            _byAlias = BuildAliasMapInternal(commands);
            _searchWords = BuildSearchWordsInternal(commands);
            _currentHash = cmdListHash;

            if (result.FromCache)
                Log.Information("CommandSearch: Loaded {Count} commands from cache in {Elapsed}ms", result.Count, result.ElapsedMs);
            else
                Log.Information("CommandSearch: Indexed {Count} commands in {Elapsed}ms", result.Count, result.ElapsedMs);
        }
        finally
        {
            _buildGate.Release();
        }
    }

    private static SearchWords BuildSearchWordsInternal(CommandEntry[] commands)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in commands)
        {
            foreach (var word in cmd.Words)
                CollectionsMarshal.GetValueRefOrAddDefault(counts, word, out _)++;
        }

        var max = Math.Max(1, (int)(commands.Length * MAX_WORD_SHARE));
        return new(commands, counts.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), max);
    }

    // A word which no command holds, or which many hold, does not move a command up.
    private sealed record SearchWords(CommandEntry[] Source, FrozenDictionary<string, int> Counts, int Max)
    {
        public bool Tells(ReadOnlySpan<char> key)
            => Counts.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(key, out var count) && count <= Max;
    }

    // "roles" and "role" are one word for the boosts.
    private static ReadOnlySpan<char> KeyOf(ReadOnlySpan<char> word)
        => word.Length > 3 && word[^1] is 's' or 'S' ? word[..^1] : word;

    private static FrozenDictionary<string, CommandEntry> BuildAliasMapInternal(CommandEntry[] commands)
    {
        var map = new Dictionary<string, CommandEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in commands)
        {
            foreach (var alias in cmd.Aliases)
                map.TryAdd(alias, cmd);
        }

        return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    // The small embedding model misses rare words, so request words which name a command or appear in its description move it up.
    public CommandSearchResult[] Search(string query, int topK = 5)
    {
        if (_index.ScoreAll(query) is not { } all)
            return [];

        var (entries, scores) = all;
        var words = new List<string>();
        AddWordsInternal(query, words, MAX_QUERY_WORDS);
        if (words.Count > 0 && _searchWords is { } known && ReferenceEquals(known.Source, entries))
            BoostInternal(entries, scores, words, known);

        var count = Math.Min(topK, entries.Length);
        var order = new int[entries.Length];
        for (var i = 0; i < order.Length; i++)
            order[i] = i;

        Array.Sort(scores, order, _descending);

        var output = new CommandSearchResult[count];
        for (var i = 0; i < count; i++)
            output[i] = new(entries[order[i]], scores[i]);
        return output;
    }

    private static void BoostInternal(CommandEntry[] entries, float[] scores, List<string> words, SearchWords known)
    {
        var present = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);

        var content = new List<string>(words.Count);
        foreach (var w in words)
        {
            var key = KeyOf(w);
            if (known.Tells(key))
                content.Add(key.Length == w.Length ? w : key.ToString());
        }

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (HasAliasInternal(entry, present, known))
                scores[i] += ALIAS_BOOST;

            if (content.Count == 0)
                continue;

            var found = 0;
            foreach (var key in content)
            {
                if (entry.Words.Contains(key))
                    found++;
            }

            scores[i] += WORD_BOOST * found / content.Count;
        }
    }

    // "todo list" counts only when the request holds both words and one of them is rare.
    private static bool HasAliasInternal(CommandEntry entry, HashSet<string> present, SearchWords known)
    {
        foreach (var tokens in entry.AliasTokens)
        {
            var all = tokens.Length > 0;
            var tells = false;
            foreach (var token in tokens)
            {
                if (token.Length < MIN_WORD_LENGTH || !present.Contains(token))
                {
                    all = false;
                    break;
                }

                tells |= known.Tells(KeyOf(token));
            }

            if (all && tells)
                return true;
        }

        return false;
    }

    private static void AddWordsInternal(string text, List<string> words, int max = int.MaxValue)
    {
        var span = text.AsSpan();
        var start = -1;
        for (var i = 0; i <= span.Length && words.Count < max; i++)
        {
            if (i < span.Length && char.IsLetterOrDigit(span[i]))
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start < 0)
                continue;

            var word = span[start..i];
            start = -1;
            if (word.Length >= MIN_WORD_LENGTH)
                words.Add(word.ToString());
        }
    }

    public CommandEntry? FindByName(string name)
        => _byAlias is { } map && map.TryGetValue(name, out var entry) ? entry : null;

    public List<string> SuggestNames(string text, int count)
    {
        var names = new List<string>(count);
        var span = text.AsSpan().Trim();
        var space = span.IndexOfAny(' ', '\n', '\t');
        var name = space < 0 ? span : span[..space];

        if (_byAlias is { } map && name.Length is > 0 and <= MAX_EDIT_NAME_LENGTH)
        {
            var maxDistance = Math.Max(1, name.Length / 3);
            var close = new List<(int Distance, string Name)>();
            foreach (var (alias, entry) in map)
            {
                if (alias.Length > MAX_EDIT_NAME_LENGTH || Math.Abs(alias.Length - name.Length) > maxDistance)
                    continue;

                var distance = EditDistanceInternal(name, alias);
                if (distance <= maxDistance)
                    close.Add((distance, entry.Aliases[0]));
            }

            close.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            foreach (var (_, n) in close)
            {
                if (names.Count >= count)
                    return names;
                if (!names.Contains(n, StringComparer.OrdinalIgnoreCase))
                    names.Add(n);
            }
        }

        foreach (var hit in Search(text, count))
        {
            if (names.Count >= count)
                break;
            if (!names.Contains(hit.Command.Aliases[0], StringComparer.OrdinalIgnoreCase))
                names.Add(hit.Command.Aliases[0]);
        }

        return names;
    }

    private static int EditDistanceInternal(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        Span<int> prev = stackalloc int[MAX_EDIT_NAME_LENGTH + 1];
        Span<int> cur = stackalloc int[MAX_EDIT_NAME_LENGTH + 1];
        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            var ca = char.ToLowerInvariant(a[i - 1]);
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = ca == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
            }

            var tmp = prev;
            prev = cur;
            cur = tmp;
        }

        return prev[b.Length];
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

    private static byte[] ComputeListHashInternal(byte[] cmdListBytes)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(cmdListBytes);
        hasher.AppendData(_indexSchemaSalt);
        return hasher.GetHashAndReset();
    }

    // run_command adds the guild prefix; an interior prefix, as in ".doas @x .give", is an argument and stays.
    private static string StripLeadingPrefixInternal(string s)
        => s.StartsWith(CommandListGenerator.DefaultPrefix, StringComparison.Ordinal)
            ? s[CommandListGenerator.DefaultPrefix.Length..]
            : s;

    private static string[] ReadStringsInternal(JsonElement cmd, string property, bool stripPrefix)
    {
        if (!cmd.TryGetProperty(property, out var el) || el.ValueKind != JsonValueKind.Array)
            return [];

        var result = new string[el.GetArrayLength()];
        var i = 0;
        foreach (var item in el.EnumerateArray())
        {
            var s = item.GetString() ?? "";
            result[i++] = stripPrefix ? StripLeadingPrefixInternal(s) : s;
        }

        return result;
    }

    private static CommandParam[][] ReadParamsInternal(JsonElement cmd)
    {
        if (!cmd.TryGetProperty("Params", out var el) || el.ValueKind != JsonValueKind.Array)
            return [];

        var overloads = new List<CommandParam[]>(el.GetArrayLength());
        foreach (var overload in el.EnumerateArray())
        {
            if (overload.ValueKind != JsonValueKind.Object)
            {
                overloads.Add([]);
                continue;
            }

            var ps = new List<CommandParam>();
            foreach (var p in overload.EnumerateObject())
                ps.Add(new(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : ""));

            overloads.Add(ps.ToArray());
        }

        return overloads.ToArray();
    }

    public static CommandEntry[] ParseCommandList(byte[] jsonBytes)
    {
        using var doc = JsonDocument.Parse(jsonBytes);

        var entries = new List<CommandEntry>();
        foreach (var module in doc.RootElement.EnumerateObject())
        {
            foreach (var cmd in module.Value.EnumerateArray())
            {
                var aliases = ReadStringsInternal(cmd, "Aliases", stripPrefix: true);

                var desc = cmd.TryGetProperty("Description", out var descEl)
                    ? descEl.GetString() ?? ""
                    : "";

                var usage = ReadStringsInternal(cmd, "Usage", stripPrefix: true);

                var submodule = cmd.TryGetProperty("Submodule", out var subEl)
                    ? subEl.GetString() ?? ""
                    : "";

                var moduleName = module.Name;
                var requirements = ReadStringsInternal(cmd, "Requirements", stripPrefix: false);

                if (aliases.Length == 0 || requirements.Contains(NoAiAgentAttribute.REQUIREMENT))
                    continue;

                var searchText = $"{string.Join(" ", aliases)} | {moduleName}/{submodule} | {desc}";

                var words = new List<string>();
                var aliasTokens = new string[aliases.Length][];
                for (var a = 0; a < aliases.Length; a++)
                {
                    aliasTokens[a] = aliases[a].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var token in aliasTokens[a])
                        words.Add(KeyOf(token).ToString());
                }

                var descWords = new List<string>();
                AddWordsInternal(desc, descWords);
                foreach (var word in descWords)
                    words.Add(KeyOf(word).ToString());

                entries.Add(new CommandEntry(
                    aliases,
                    desc,
                    usage,
                    ReadParamsInternal(cmd),
                    ReadStringsInternal(cmd, "Options", stripPrefix: false),
                    moduleName,
                    submodule,
                    requirements,
                    searchText)
                {
                    Words = words.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                    AliasTokens = aliasTokens
                });
            }
        }

        return entries.ToArray();
    }
}

public sealed record CommandParam(string Name, string Desc);

// Usage holds the examples, and their count can differ from the count of Params overloads.
public sealed record CommandEntry(
    string[] Aliases,
    string Description,
    string[] Usage,
    CommandParam[][] Params,
    string[] Options,
    string Module,
    string Submodule,
    string[] Requirements,
    string SearchText)
{
    public FrozenSet<string> Words { get; init; } = FrozenSet<string>.Empty;

    public string[][] AliasTokens { get; init; } = [];
}

public sealed record CommandSearchResult(CommandEntry Command, float Score);
