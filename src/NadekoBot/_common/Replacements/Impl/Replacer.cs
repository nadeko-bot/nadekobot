using System.Text;
using System.Text.RegularExpressions;

namespace NadekoBot.Common;

public sealed partial class Replacer
{
    private readonly ReplacementInfo[] _baseReps;
    private readonly RegexReplacementInfo[] _baseRegexReps;
    private readonly IReadOnlyList<ReplacementInfo> _overrides;
    private readonly IReadOnlyList<RegexReplacementInfo> _regexOverrides;
    private readonly object?[] _inputData;

    public Replacer(
        ReplacementInfo[] baseReps,
        RegexReplacementInfo[] baseRegexReps,
        IReadOnlyList<ReplacementInfo> overrides,
        IReadOnlyList<RegexReplacementInfo> regexOverrides,
        object?[] inputData)
    {
        _baseReps = baseReps;
        _baseRegexReps = baseRegexReps;
        _overrides = overrides;
        _regexOverrides = regexOverrides;
        _inputData = inputData;
    }

    // One pass over the template: a value which a placeholder inserts (a member's text, a name) is never scanned
    // again, so it cannot form a new placeholder or a mention, for example "@every%rng1-1%one".
    public async ValueTask<string?> ReplaceAsync(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var spans = FindRegexSpansInternal(input);
        var firstPct = input.IndexOf('%');
        if (firstPct < 0 && spans is null)
            return input;

        var map = firstPct >= 0 ? BuildTokenMap().GetAlternateLookup<ReadOnlySpan<char>>() : default;
        var hasTokens = firstPct >= 0;

        var sb = new StringBuilder(input.Length);
        var i = 0;
        var s = 0;
        while (i < input.Length)
        {
            // A token which was replaced can cover the start of a regex match.
            while (spans is not null && s < spans.Count && spans[s].Match.Index < i)
                s++;

            var nextSpan = spans is not null && s < spans.Count ? spans[s].Match.Index : -1;
            var nextPct = hasTokens ? input.IndexOf('%', i) : -1;
            var next = nextSpan < 0 ? nextPct : nextPct < 0 ? nextSpan : Math.Min(nextPct, nextSpan);
            if (next < 0)
            {
                sb.Append(input, i, input.Length - i);
                break;
            }

            sb.Append(input, i, next - i);
            i = next;

            if (next == nextPct)
            {
                var end = input.IndexOf('%', i + 1);
                if (end >= 0 && map.TryGetValue(input.AsSpan(i, end - i + 1), out var rep))
                {
                    var value = await rep.GetValueAsync(_inputData);
                    if (value is not null)
                        sb.Append(value);
                    i = end + 1;
                    continue;
                }
            }

            if (next == nextSpan)
            {
                var (match, regexRep) = spans![s++];
                sb.Append(await regexRep.GetValueAsync(match, _inputData));
                i = match.Index + match.Length;
                continue;
            }

            sb.Append('%');
            i++;
        }

        return sb.ToString();
    }

    // Each regex placeholder replaces its first match in the template. Overrides come first and win an overlap.
    private List<(Match Match, RegexReplacementInfo Rep)>? FindRegexSpansInternal(string input)
    {
        List<(Match Match, RegexReplacementInfo Rep)>? spans = null;

        for (var i = 0; i < _regexOverrides.Count; i++)
            AddSpanInternal(ref spans, input, _regexOverrides[i]);

        for (var i = 0; i < _baseRegexReps.Length; i++)
        {
            var rep = _baseRegexReps[i];
            if (!IsRegexOverridden(rep.Pattern))
                AddSpanInternal(ref spans, input, rep);
        }

        spans?.Sort(static (a, b) => a.Match.Index.CompareTo(b.Match.Index));
        return spans;
    }

    private static void AddSpanInternal(
        ref List<(Match Match, RegexReplacementInfo Rep)>? spans,
        string input,
        RegexReplacementInfo rep)
    {
        var match = rep.Regex.Match(input);
        if (!match.Success)
            return;

        if (spans is not null)
        {
            var end = match.Index + match.Length;
            foreach (var (other, _) in spans)
            {
                if (match.Index < other.Index + other.Length && other.Index < end)
                    return;
            }
        }

        (spans ??= new(2)).Add((match, rep));
    }

    // Tokens are ASCII, so an ordinal comparer is exact and allows span lookups.
    private Dictionary<string, ReplacementInfo> BuildTokenMap()
    {
        var map = new Dictionary<string, ReplacementInfo>(
            _baseReps.Length + _overrides.Count,
            StringComparer.Ordinal);

        for (var i = 0; i < _baseReps.Length; i++)
            map[_baseReps[i].Token] = _baseReps[i];

        for (var i = 0; i < _overrides.Count; i++)
            map[_overrides[i].Token] = _overrides[i];

        return map;
    }

    private bool IsRegexOverridden(string pattern)
    {
        for (var i = 0; i < _regexOverrides.Count; i++)
        {
            if (string.Equals(_regexOverrides[i].Pattern, pattern, StringComparison.InvariantCulture))
                return true;
        }

        return false;
    }

    public async ValueTask<SmartText> ReplaceAsync(SmartText data)
        => data switch
        {
            SmartPlainText plain => await ReplaceAsync(plain),
            SmartEmbedTextArray arr => await ReplaceAsync(arr),
            _ => throw new ArgumentOutOfRangeException(nameof(data), "Unsupported argument type")
        };

    private async Task<SmartEmbedTextArray> ReplaceAsync(SmartEmbedTextArray embedArr)
        => new()
        {
            Embeds = await embedArr.Embeds.Map(async e => await ReplaceAsync(e) with
                                   {
                                       Color = e.Color
                                   })
                                   .WhenAll(),
            Content = await ReplaceAsync(embedArr.Content)
        };

    private async ValueTask<SmartPlainText> ReplaceAsync(SmartPlainText plain)
        => await ReplaceAsync(plain.Text);

    private async Task<T> ReplaceAsync<T>(T embedData)
        where T : SmartEmbedTextBase, new()
    {
        var newEmbedData = new T
        {
            Description = await ReplaceAsync(embedData.Description),
            Title = await ReplaceAsync(embedData.Title),
            Thumbnail = await ReplaceAsync(embedData.Thumbnail),
            Image = await ReplaceAsync(embedData.Image),
            Url = await ReplaceAsync(embedData.Url),
            Timestamp = embedData.Timestamp,
            Author = embedData.Author is null
                ? null
                : new()
                {
                    Name = await ReplaceAsync(embedData.Author.Name),
                    Url = await ReplaceAsync(embedData.Author.Url),
                    IconUrl = await ReplaceAsync(embedData.Author.IconUrl)
                },
            Fields = await Task.WhenAll(embedData
                                        .Fields?
                                        .Map(async f => new SmartTextEmbedField
                                        {
                                            Name = await ReplaceAsync(f.Name),
                                            Value = await ReplaceAsync(f.Value),
                                            Inline = f.Inline
                                        })
                                        ?? []),
            Footer = embedData.Footer is null
                ? null
                : new()
                {
                    Text = await ReplaceAsync(embedData.Footer.Text),
                    IconUrl = await ReplaceAsync(embedData.Footer.IconUrl)
                }
        };

        return newEmbedData;
    }
}