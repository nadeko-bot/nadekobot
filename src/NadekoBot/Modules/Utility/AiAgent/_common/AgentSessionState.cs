using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent;

// The loop runs one tool call at a time, so it needs no lock.
public sealed class AgentSessionState
{
    public const int MAX_COMMAND_RUNS = 10;
    public const int MAX_SEARCHES_PER_TOOL = 3;
    public const int MAX_FAILURES_IN_A_ROW = 3;
    public const int MAX_MESSAGE_ACTIONS = 5;
    public const int MAX_LISTED_STEPS = 10;
    public const int MAX_LISTED_FAILURES = 5;
    public const string DUPLICATE_STATUS = "duplicate";
    public const string BAD_ARGS_STATUS = "bad_args";

    // The largest channel memory, and a message with its reply, embeds and files.
    private const int MAX_HISTORY_MESSAGES = 100;
    private const int MAX_MESSAGE_CHARS = 25_000;
    private const int MAX_UNTRUSTED_CHARS = MAX_HISTORY_MESSAGES * MAX_MESSAGE_CHARS;

    // NFKC can make a text 18 times longer ("ﷺ"), and a text with invisible signs is kept twice.
    private const int MAX_KEPT_CHARS = 2 * MAX_UNTRUSTED_CHARS;

    // A message edit may fix only the letter case, so the message tools compare their calls in exact case.
    private readonly Dictionary<string, string> _messageActions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _searches = new(StringComparer.Ordinal);
    private readonly GuardTexts _history = new();
    private readonly GuardTexts _untrusted = new();
    private readonly List<AgentDoneStep> _done = [];
    private readonly List<AgentFailedStep> _failed = [];
    private readonly HashSet<string> _doneLeftOut = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AgentFailedStep> _failedLeftOut = [];
    private bool _dropLogged;

    public int CommandRuns { get; private set; }

    public int MessageActions { get; private set; }

    public bool LimitReached { get; set; }

    public bool AnythingShown { get; set; }

    // A command worked without a recorded message, for example a game which posts its own table.
    public bool RanQuietly { get; set; }

    // A tool which knows more than its result text sets this for the call which runs now.
    public AgentCallReport? Report { get; private set; }

    // The command which runs now. A request which stops while it waits does not know if it ran.
    public string? InFlight { get; set; }

    public IReadOnlyList<AgentDoneStep> Done
        => _done;

    public IReadOnlyList<AgentFailedStep> Failed
        => _failed;

    public int DoneLeftOut
        => _doneLeftOut.Count;

    public int FailedLeftOut
        => _failedLeftOut.Count;

    public void BeginCall()
        => Report = null;

    public void ReportCall(AgentCallReport report)
        => Report = report;

    public string? FindMessageAction(string key)
        => _messageActions.GetValueOrDefault(key);

    public void RecordMessageAction(string key, string result)
        => _messageActions[key] = result;

    public bool TryUseCommandRun()
    {
        if (CommandRuns >= MAX_COMMAND_RUNS)
            return false;

        CommandRuns++;
        return true;
    }

    public bool TryUseMessageAction()
    {
        if (MessageActions >= MAX_MESSAGE_ACTIONS)
            return false;

        MessageActions++;
        return true;
    }

    public bool TryUseSearch(string tool)
    {
        ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(_searches, tool, out _);
        if (count >= MAX_SEARCHES_PER_TOOL)
            return false;

        count++;
        return true;
    }

    // Command output can quote other people, so it is untrusted too. The guard reads it as the model does, without tags.
    public void AddOutput(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            AddUntrusted(PromptSanitizer.Sanitize(text));
    }

    // The texts of the prompt have their own cap, so tool results cannot push them out.
    public void AddUntrusted(string text, bool inPrompt = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var texts = inPrompt ? _history : _untrusted;
        var joined = GuardText.Normalize(text, out var other);
        texts.Add(joined, text.Length);

        // A hidden sign can join two words (".slot\u200Bplease") or split one, so the guard keeps both readings when they differ.
        if (other && GuardText.NormalizeOther(text) is var second && !string.Equals(second, joined, StringComparison.Ordinal))
            texts.Add(second, 0);

        if (Overflowed && !_dropLogged)
        {
            _dropLogged = true;
            Log.Warning("AI agent dropped untrusted texts at the cap, so commands take only the member's arguments, and currency commands run only by name");
        }
    }

    // Texts dropped at the cap, which the model may still read, so the checks refuse what they cannot see.
    public bool Overflowed
        => _history.Dropped || _untrusted.Dropped;

    // Takes the sanitized text which the model reads. JSON escapes ("\n", "\uD83D") would hide a name, so it reads the values.
    public void AddToolResult(string result)
    {
        // Prose results such as "[message 1] ..." also start with "[".
        var start = result.AsSpan().TrimStart();
        if (!(start is ['{', ..] || (start is ['[', .. var items] && items.TrimStart() is ['{' or '"' or '[' or ']', ..])))
        {
            AddUntrusted(result);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(result);
            var sb = new StringBuilder(result.Length);
            AppendValuesInternal(sb, doc.RootElement);
            AddUntrusted(sb.ToString());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            AddUntrusted(result);
        }
    }

    private static void AppendValuesInternal(StringBuilder sb, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    sb.Append(property.Name).Append('\n');
                    AppendValuesInternal(sb, property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    AppendValuesInternal(sb, item);
                break;
            case JsonValueKind.String:
                sb.Append(element.GetString()).Append('\n');
                break;
            case JsonValueKind.Number:
                sb.Append(element.GetRawText()).Append('\n');
                break;
        }
    }

    // True when one untrusted text calls the command with a prefix, as in ".slot".
    public bool UntrustedTextCalls(IReadOnlyList<string> aliases, string prefix)
        => Overflowed || CallsInternal(_history.Texts, aliases, prefix) || CallsInternal(_untrusted.Texts, aliases, prefix);

    private static bool CallsInternal(List<string> texts, IReadOnlyList<string> aliases, string prefix)
    {
        foreach (var text in texts)
        {
            if (GuardText.CallsCommand(text, aliases, prefix))
                return true;
        }

        return false;
    }

    // True when one untrusted text names the command with all of its argument tokens.
    public bool UntrustedTextAsksFor(IReadOnlyList<string> aliases, IReadOnlyList<string> tokens, string prefix)
        => Overflowed
           || AsksForInternal(_history.Texts, aliases, tokens, prefix)
           || AsksForInternal(_untrusted.Texts, aliases, tokens, prefix);

    public bool UntrustedTextHolds(string token)
        => Overflowed || HoldsInternal(_history.Texts, token) || HoldsInternal(_untrusted.Texts, token);

    private sealed class GuardTexts
    {
        private readonly List<int> _raw = [];
        private int _rawChars;
        private int _keptChars;

        public List<string> Texts { get; } = [];

        public bool Dropped { get; private set; }

        public void Add(string text, int raw)
        {
            Texts.Add(text);
            _raw.Add(raw);
            _rawChars += raw;
            _keptChars += text.Length;
            while ((_rawChars > MAX_UNTRUSTED_CHARS || _keptChars > MAX_KEPT_CHARS) && Texts.Count > 1)
            {
                _rawChars -= _raw[0];
                _keptChars -= Texts[0].Length;
                _raw.RemoveAt(0);
                Texts.RemoveAt(0);
                Dropped = true;
            }
        }
    }

    private static bool AsksForInternal(List<string> texts, IReadOnlyList<string> aliases, IReadOnlyList<string> tokens, string prefix)
    {
        foreach (var text in texts)
        {
            if (!GuardText.NamesCommand(text, aliases, prefix))
                continue;

            var all = true;
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!GuardText.ContainsPhrase(text, tokens[i]))
                {
                    all = false;
                    break;
                }
            }

            if (all)
                return true;
        }

        return false;
    }

    private static bool HoldsInternal(List<string> texts, string token)
    {
        foreach (var text in texts)
        {
            if (GuardText.ContainsPhrase(text, token))
                return true;
        }

        return false;
    }

    // A success of the same call, or of the same command after bad arguments, fixes the failure.
    public void AddDone(string label, string command)
    {
        RemoveFixedInternal(_failed, label, command);
        RemoveFixedInternal(_failedLeftOut, label, command);
        while (_failed.Count < MAX_LISTED_FAILURES && _failedLeftOut.Count > 0)
        {
            _failed.Add(_failedLeftOut[0]);
            _failedLeftOut.RemoveAt(0);
        }

        for (var i = 0; i < _done.Count; i++)
        {
            if (string.Equals(_done[i].Label, label, StringComparison.OrdinalIgnoreCase))
            {
                _done[i] = _done[i] with { Runs = _done[i].Runs + 1 };
                return;
            }
        }

        if (_done.Count < MAX_LISTED_STEPS)
            _done.Add(new(label, 1));
        else
            _doneLeftOut.Add(label);
    }

    public void AddFailed(string label, string command, string reason, string status)
    {
        if (HasLabelInternal(_failed, label) || HasLabelInternal(_failedLeftOut, label))
            return;

        if (_failed.Count < MAX_LISTED_FAILURES)
            _failed.Add(new(label, command, reason, status));
        else
            _failedLeftOut.Add(new(label, command, reason, status));
    }

    private static void RemoveFixedInternal(List<AgentFailedStep> failures, string label, string command)
    {
        for (var i = failures.Count - 1; i >= 0; i--)
        {
            var failed = failures[i];
            if (string.Equals(failed.Label, label, StringComparison.OrdinalIgnoreCase)
                || (failed.Status == BAD_ARGS_STATUS && string.Equals(failed.Command, command, StringComparison.OrdinalIgnoreCase)))
                failures.RemoveAt(i);
        }
    }

    private static bool HasLabelInternal(List<AgentFailedStep> failures, string label)
    {
        foreach (var f in failures)
        {
            if (string.Equals(f.Label, label, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

public readonly record struct AgentDoneStep(string Label, int Runs);

public readonly record struct AgentFailedStep(string Label, string Command, string Reason, string Status);

// Detail is a short label for the trace log, never text which a member wrote.
public readonly record struct AgentCallReport(bool Ok, bool Final, string Status, string? Detail = null);
