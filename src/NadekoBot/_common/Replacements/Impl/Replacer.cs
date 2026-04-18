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

        for (var i = 0; i < _overrides.Count; i++)
        {
            var rep = _overrides[i];
            if (input.Contains(rep.Token, StringComparison.InvariantCulture))
            {
                var objs = GetParams(rep);
                input = input.Replace(rep.Token, await rep.GetValueAsync(objs), StringComparison.InvariantCulture);
            }
        }

        for (var i = 0; i < _baseReps.Length; i++)
        {
            var rep = _baseReps[i];
            if (IsOverridden(rep.Token))
                continue;

            if (input.Contains(rep.Token, StringComparison.InvariantCulture))
            {
                var objs = GetParams(rep);
                input = input.Replace(rep.Token, await rep.GetValueAsync(objs), StringComparison.InvariantCulture);
            }
        }

        for (var i = 0; i < _regexOverrides.Count; i++)
        {
            var rep = _regexOverrides[i];
            input = await ApplyRegexReplacementAsync(input, rep);
        }

        for (var i = 0; i < _baseRegexReps.Length; i++)
        {
            var rep = _baseRegexReps[i];
            if (IsRegexOverridden(rep.Pattern))
                continue;

            input = await ApplyRegexReplacementAsync(input, rep);
        }

        return input;
    }

    private async ValueTask<string> ApplyRegexReplacementAsync(string input, RegexReplacementInfo rep)
    {
        var objs = GetParams(rep);
        var match = rep.Regex.Match(input);
        if (match.Success)
        {
            var sb = new StringBuilder();
            sb.Append(input, 0, match.Index)
              .Append(await rep.GetValueAsync(match, objs));

            var lastIndex = match.Index + match.Length;
            sb.Append(input, lastIndex, input.Length - lastIndex);
            return sb.ToString();
        }

        return input;
    }

    private bool IsOverridden(string token)
    {
        for (var i = 0; i < _overrides.Count; i++)
        {
            if (string.Equals(_overrides[i].Token, token, StringComparison.InvariantCulture))
                return true;
        }

        return false;
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

    private object?[]? GetParams(ReplacementInfo rep)
    {
        var slots = rep.ParamSlotIndices;
        if (slots.Length == 0)
            return null;

        var objs = new object?[slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            objs[i] = slot >= 0 ? _inputData[slot] : null;
        }

        return objs;
    }

    private object?[]? GetParams(RegexReplacementInfo rep)
    {
        var slots = rep.ParamSlotIndices;
        if (slots.Length == 0)
            return null;

        var objs = new object?[slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            objs[i] = slot >= 0 ? _inputData[slot] : null;
        }

        return objs;
    }

    public async ValueTask<SmartText> ReplaceAsync(SmartText data)
        => data switch
        {
            SmartEmbedText embedData => await ReplaceAsync(embedData) with
            {
                PlainText = await ReplaceAsync(embedData.PlainText),
                Color = embedData.Color
            },
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
            Author = embedData.Author is null
                ? null
                : new()
                {
                    Name = await ReplaceAsync(embedData.Author.Name),
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