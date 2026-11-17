using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using NadekoBot.AgentEval;

var mode = "all";
string? outPath = null;
var label = "current";
string? only = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out":
            outPath = Path.GetFullPath(args[++i]);
            break;
        case "--label":
            label = args[++i];
            break;
        case "--only":
            only = args[++i];
            break;
        default:
            mode = args[i];
            break;
    }
}

var repo = FindRepoRootInternal();
var commandList = Path.Combine(repo, "src/NadekoBot/data/commandlist.json");
PrepareWorkDirInternal(repo);

var services = await EvalServices.CreateAsync(commandList);
var known = CommandSearchService.ParseCommandList(await File.ReadAllBytesAsync(commandList))
                                .Select(static c => c.Aliases[0])
                                .ToHashSet(StringComparer.Ordinal);

if (mode == "scores")
{
    Console.WriteLine(SearchQuality.ScoreTable(services.Search));
    return 0;
}

SearchReport? searchReport = null;
SearchReport? holdoutReport = null;
SearchReport? unrelatedReport = null;
if (mode is "all" or "search")
{
    searchReport = SearchQuality.Run(services.Search, known, SearchQuality.Cases);
    holdoutReport = SearchQuality.Run(services.Search, known, SearchQuality.Holdout);
    unrelatedReport = SearchQuality.RunUnrelated(services.Search, AgentHitFormatter.CANDIDATES);
}

CacheReport? cacheReport = null;
if (mode is "all" or "cache")
{
    cacheReport = await CacheCheck.RunAsync(services);
    Console.Error.WriteLine($"cache: {cacheReport.Failure ?? "pass"} ({cacheReport.SharedChars} of {cacheReport.TotalChars} characters shared)");
}

var loops = new List<(Scenario Scenario, LoopOutcome Outcome, string? Failure)>();
if (mode is "all" or "loop")
{
    foreach (var s in Scenarios.All())
    {
        if (only is not null && !s.Id.Equals(only, StringComparison.Ordinal))
            continue;

        var outcome = await AgentHarness.RunAsync(s, services);
        loops.Add((s, outcome, s.Check(outcome)));
        Console.Error.WriteLine($"{s.Id}: {(s.Check(outcome) is { } f ? "FAIL " + f : "pass")} ({outcome.ElapsedMs} ms)");
    }
}

var md = BuildMarkdownInternal(label, cacheReport, searchReport, holdoutReport, unrelatedReport, loops);
Console.WriteLine(md);

if (outPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    await File.WriteAllTextAsync(outPath + ".md", md);
    var json = JsonSerializer.Serialize(new
        {
            label,
            search = searchReport,
            holdout = holdoutReport,
            unrelated = unrelatedReport,
            cache = cacheReport,
            loops = loops.Select(static l => new
            {
                id = l.Scenario.Id,
                pass = l.Failure is null,
                failure = l.Failure,
                l.Outcome.ModelRequests,
                l.Outcome.ToolsOffRequests,
                runs = l.Outcome.Server.Executed,
                dispatched = l.Outcome.Server.Dispatched,
                commandMessages = l.Outcome.Server.CommandMessages.Count,
                agentMessages = l.Outcome.Server.AgentMessages.Count,
                l.Outcome.FinalPosted,
                l.Outcome.Final,
                l.Outcome.Ending,
                l.Outcome.PrefixStable,
                l.Outcome.ElapsedMs
            })
        },
        new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    await File.WriteAllTextAsync(outPath + ".json", json);
}

return 0;

static string FindRepoRootInternal()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NadekoBot.sln")))
                return dir.FullName;
        }
    }

    throw new InvalidOperationException("Run the evaluation inside the NadekoBot repository.");
}

// The bot reads the model and writes the index cache relative to the working folder.
static void PrepareWorkDirInternal(string repo)
{
    var work = Path.Combine(Path.GetTempPath(), "nadeko-agent-eval");
    var modelDst = Path.Combine(work, "data/ai/models/all-MiniLM-L6-v2");
    var modelSrc = Path.Combine(repo, "src/NadekoBot/bin/Debug/net9.0/data/ai/models/all-MiniLM-L6-v2");
    Directory.CreateDirectory(modelDst);

    foreach (var file in new[] { "model.onnx", "vocab.txt" })
    {
        var dst = Path.Combine(modelDst, file);
        if (File.Exists(dst))
            continue;

        var src = Path.Combine(modelSrc, file);
        if (!File.Exists(src))
            throw new FileNotFoundException("Start the bot once in Debug, so that it downloads the embedding model.", src);

        File.Copy(src, dst);
    }

    Directory.SetCurrentDirectory(work);
}

static string BuildMarkdownInternal(
    string label,
    CacheReport? cache,
    SearchReport? search,
    SearchReport? holdout,
    SearchReport? unrelated,
    List<(Scenario Scenario, LoopOutcome Outcome, string? Failure)> loops)
{
    var sb = new StringBuilder();
    sb.AppendLine($"# Agent evaluation: {label}");
    sb.AppendLine();

    if (loops.Count > 0)
    {
        var passed = loops.Count(static l => l.Failure is null);
        sb.AppendLine($"## Loop scenarios: {passed} of {loops.Count} pass");
        sb.AppendLine();
        sb.AppendLine($"Model requests: {loops.Sum(static l => l.Outcome.ModelRequests)}. Command runs: {loops.Sum(static l => l.Outcome.TotalRuns)}. Messages the members see: {loops.Sum(static l => l.Outcome.Messages)}. Prefix stable in all: {loops.All(static l => l.Outcome.PrefixStable)}.");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Expected | Requests | Runs | Messages | Ending | Time (ms) | Result |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var (s, o, f) in loops)
        {
            sb.AppendLine(
                $"| {s.Id} | {s.Expected} | {o.ModelRequests} | {o.TotalRuns} | {o.Messages} | {o.Ending} | {o.ElapsedMs} | {(f is null ? "pass" : "FAIL: " + Escape(f))} |");
        }

        sb.AppendLine();
    }

    if (cache is not null)
    {
        sb.AppendLine($"## Prompt cache: {(cache.Failure is null ? "pass" : "FAIL: " + Escape(cache.Failure))}");
        sb.AppendLine();
        sb.AppendLine($"In the second request of a member (3 new messages in the channel, the clock 30 s later), the first {cache.SharedChars} of {cache.TotalChars} characters of the tools and messages JSON are the same as in the first request. The first difference is in the {cache.FirstDifference} part.");
        sb.AppendLine();
        sb.AppendLine("| Part | Characters | Same in request 2 |");
        sb.AppendLine("|---|---|---|");
        foreach (var part in cache.Parts)
            sb.AppendLine($"| {part.Name} | {part.Chars} | {(part.Same ? "yes" : "no")} |");
        sb.AppendLine();
    }

    AppendSearchInternal(sb, "Command search", search);
    AppendSearchInternal(sb, "Command search, holdout set", holdout);
    if (unrelated is not null)
    {
        sb.AppendLine($"## Command search, short aliases in other words: {unrelated.Top1} of {unrelated.Cases} clean");
        sb.AppendLine();
        sb.AppendLine("| Request | Must not be a candidate | Candidates |");
        sb.AppendLine("|---|---|---|");
        foreach (var m in unrelated.Misses)
            sb.AppendLine($"| {Escape(m.Query)} | {string.Join(", ", m.Expected)} | {string.Join(", ", m.Got)} |");
        sb.AppendLine();
    }

    return sb.ToString();

    static void AppendSearchInternal(StringBuilder sb, string title, SearchReport? search)
    {
        if (search is null)
            return;

        sb.AppendLine($"## {title}: top-1 {search.Top1}/{search.Cases}, top-5 {search.Top5}/{search.Cases}");
        sb.AppendLine();
        sb.AppendLine($"Average search time: {search.AvgMs:F1} ms.");
        if (search.UnknownLabels.Count > 0)
            sb.AppendLine($"Labels which are not commands: {string.Join(", ", search.UnknownLabels)}.");
        sb.AppendLine();
        sb.AppendLine("| Request (top-1 miss) | Expected | Top 5 |");
        sb.AppendLine("|---|---|---|");
        foreach (var m in search.Misses)
            sb.AppendLine($"| {Escape(m.Query)} | {string.Join(", ", m.Expected)} | {string.Join(", ", m.Got)} |");
        sb.AppendLine();
    }

    static string Escape(string s)
        => s.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
