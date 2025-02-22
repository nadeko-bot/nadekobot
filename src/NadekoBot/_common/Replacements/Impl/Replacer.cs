using System.Text;
using System.Text.RegularExpressions;

namespace NadekoBot.Common;

public sealed partial class Replacer
{
    private readonly IEnumerable<ReplacementInfo> _reps;
    private readonly IEnumerable<RegexReplacementInfo> _regexReps;
    private readonly object[] _inputData;

    // [GeneratedRegex(@"\%[\p{L}\p{N}\._]*[\p{L}\p{N}]+[\p{L}\p{N}\._]*\%")]
    // private static partial Regex TokenExtractionRegex();

    public Replacer(IEnumerable<ReplacementInfo> reps, IEnumerable<RegexReplacementInfo> regexReps, object[] inputData)
    {
        _reps = reps;
        _inputData = inputData;
        _regexReps = regexReps;
    }

    public async ValueTask<string?> ReplaceAsync(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // var matches = TokenExtractionRegex().IsMatch(input);

        // if (matches)
        // {
        foreach (var rep in _reps)
        {
            if (input.Contains(rep.Token, StringComparison.InvariantCulture))
            {
                var objs = GetParams(rep.InputTypes);
                input = input.Replace(rep.Token, await rep.GetValueAsync(objs), StringComparison.InvariantCulture);
            }
        }
        // }

        foreach (var rep in _regexReps)
        {
            var sb = new StringBuilder();

            var objs = GetParams(rep.InputTypes);
            var match = rep.Regex.Match(input);
            if (match.Success)
            {
                sb.Append(input, 0, match.Index)
                  .Append(await rep.GetValueAsync(match, objs));

                var lastIndex = match.Index + match.Length;
                sb.Append(input, lastIndex, input.Length - lastIndex);
                input = sb.ToString();
            }
        }

        return input;
    }

    private object?[]? GetParams(IReadOnlyCollection<Type> inputTypes)
    {
        if (inputTypes.Count == 0)
            return null;

        var objs = new List<object>();
        foreach (var t in inputTypes)
        {
            var datum = _inputData.FirstOrDefault(x => x.GetType().IsAssignableTo(t));
            if (datum is not null)
                objs.Add(datum);
        }

        return objs.ToArray();
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