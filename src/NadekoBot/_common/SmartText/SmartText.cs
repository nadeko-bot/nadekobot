#nullable disable
using Newtonsoft.Json.Linq;
using System.Text.Json.Serialization;

namespace NadekoBot;

public abstract record SmartText
{
    [JsonIgnore]
    public bool IsPlainText
        => this is SmartPlainText;

    [JsonIgnore]
    public bool IsEmbedArray
        => this is SmartEmbedTextArray;

    public static implicit operator SmartText(string input)
        => new SmartPlainText(input);

    public static SmartText operator +(SmartText text, string input)
        => text switch
        {
            SmartPlainText spt => new SmartPlainText(spt.Text + input),
            SmartEmbedTextArray arr => arr with
            {
                Content = arr.Content + input
            },
            _ => throw new ArgumentOutOfRangeException(nameof(text))
        };

    public static SmartText operator +(string input, SmartText text)
        => text switch
        {
            SmartPlainText spt => new SmartPlainText(input + spt.Text),
            SmartEmbedTextArray arr => arr with
            {
                Content = input + arr.Content
            },
            _ => throw new ArgumentOutOfRangeException(nameof(text))
        };

    public static SmartText CreateFrom(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new SmartPlainText(input);

        try
        {
            var doc = JObject.Parse(input);

            // New canonical shape -- array of embeds with optional content.
            if (doc.TryGetValue("embeds", out _))
            {
                var arr = doc.ToObject<SmartEmbedTextArray>();
                if (arr is null || !arr.IsValid)
                    return new SmartPlainText(input);

                arr.NormalizeFields();
                return arr;
            }

            // Legacy single-embed shape -- normalize to the array form so the
            // rest of the codebase only ever sees SmartEmbedTextArray.
            var legacy = doc.ToObject<LegacySmartEmbedText>();
            if (legacy is null || !(legacy.IsValid || !string.IsNullOrWhiteSpace(legacy.PlainText)))
                return new SmartPlainText(input);

            var converted = legacy.ToArray();
            converted.NormalizeFields();
            return converted;
        }
        catch
        {
            return new SmartPlainText(input);
        }
    }
}
