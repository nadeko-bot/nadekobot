using System.Text;

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

    public async ValueTask<string?> ReplaceAsync(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var firstPct = input.IndexOf('%');
        if (firstPct >= 0)
            input = await ApplyTokenReplacementsInternalAsync(input, firstPct);

        for (var i = 0; i < _regexOverrides.Count; i++)
            input = await ApplyRegexReplacementInternalAsync(input, _regexOverrides[i]);

        for (var i = 0; i < _baseRegexReps.Length; i++)
        {
            var rep = _baseRegexReps[i];
            if (IsRegexOverridden(rep.Pattern))
                continue;

            input = await ApplyRegexReplacementInternalAsync(input, rep);
        }

        return input;
    }

    private async ValueTask<string> ApplyTokenReplacementsInternalAsync(string input, int firstPct)
    {
        var map = BuildTokenMap();

        var sb = new StringBuilder(input.Length);
        sb.Append(input, 0, firstPct);

        var i = firstPct;
        while (i < input.Length)
        {
            // Bulk-copy everything up to the next '%'
            var nextPct = input.IndexOf('%', i);
            if (nextPct < 0)
            {
                sb.Append(input, i, input.Length - i);
                break;
            }

            if (nextPct > i)
            {
                sb.Append(input, i, nextPct - i);
                i = nextPct;
            }

            // Find the closing '%'
            var end = input.IndexOf('%', i + 1);
            if (end < 0)
            {
                sb.Append(input, i, input.Length - i);
                break;
            }

            var token = input.Substring(i, end - i + 1);

            if (map.TryGetValue(token, out var rep))
            {
                var value = await rep.GetValueAsync(_inputData);
                if (value is not null)
                    sb.Append(value);
                i = end + 1;
            }
            else
            {
                sb.Append('%');
                i++;
            }
        }

        return sb.ToString();
    }

    private Dictionary<string, ReplacementInfo> BuildTokenMap()
    {
        var map = new Dictionary<string, ReplacementInfo>(
            _baseReps.Length + _overrides.Count,
            StringComparer.InvariantCulture);

        for (var i = 0; i < _baseReps.Length; i++)
            map[_baseReps[i].Token] = _baseReps[i];

        for (var i = 0; i < _overrides.Count; i++)
            map[_overrides[i].Token] = _overrides[i];

        return map;
    }

    private async ValueTask<string> ApplyRegexReplacementInternalAsync(string input, RegexReplacementInfo rep)
    {
        var match = rep.Regex.Match(input);
        if (match.Success)
        {
            var sb = new StringBuilder();
            sb.Append(input, 0, match.Index)
              .Append(await rep.GetValueAsync(match, _inputData));

            var lastIndex = match.Index + match.Length;
            sb.Append(input, lastIndex, input.Length - lastIndex);
            return sb.ToString();
        }

        return input;
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