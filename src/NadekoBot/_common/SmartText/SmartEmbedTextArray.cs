#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot;

public sealed record SmartEmbedTextArray : SmartText
{
    public string Content { get; set; }
    public SmartEmbedArrayElementText[] Embeds { get; set; }

    [JsonIgnore]
    public bool IsValid
        => Embeds?.All(x => x.IsValid) ?? false;

    public EmbedBuilder[] GetEmbedBuilders()
    {
        if (Embeds is null)
            return Array.Empty<EmbedBuilder>();

        return Embeds
            .Where(x => x.IsValid)
            .Select(em => em.GetEmbed())
            .ToArray();
    }

    public void NormalizeFields()
    {
        if (Embeds is null)
            return;
        
        foreach(var eb in Embeds)
            eb.NormalizeFields();
    }
}