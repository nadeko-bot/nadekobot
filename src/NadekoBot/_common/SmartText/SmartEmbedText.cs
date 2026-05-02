#nullable disable warnings
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json.Serialization;

namespace NadekoBot;

public sealed record SmartEmbedArrayElementText : SmartEmbedTextBase
{
    public string Color { get; init; } = string.Empty;

    public SmartEmbedArrayElementText()
    {
    }

    public SmartEmbedArrayElementText(IEmbed eb) : base(eb)
    {
        Color = eb.Color is { } c ? "#" + new Rgba32(c.R, c.G, c.B).ToHex()[..6] : string.Empty;
    }

    protected override EmbedBuilder GetEmbedInternal()
    {
        var embed = base.GetEmbedInternal();
        if (TryParseColor(Color, out var color))
            return embed.WithColor(color);

        return embed;
    }

    private static bool TryParseColor(string value, out Discord.Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var span = value.AsSpan().TrimStart('#');
        if (Rgba32.TryParseHex(span.ToString(), out var rgba))
        {
            color = new Discord.Color(rgba.R, rgba.G, rgba.B);
            return true;
        }

        return false;
    }
}

/// <summary>
/// Legacy single-embed JSON shape kept around solely for deserializing user templates
/// saved before the array format existed. Not a <see cref="SmartText"/>; converted to
/// <see cref="SmartEmbedTextArray"/> on parse via <see cref="ToArray"/>.
/// </summary>
internal sealed record LegacySmartEmbedText
{
    public string Title { get; init; }
    public string Description { get; init; }
    public string Url { get; init; }
    public string Thumbnail { get; init; }
    public string Image { get; init; }
    public string Timestamp { get; init; }
    public SmartTextEmbedAuthor Author { get; init; }
    public SmartTextEmbedFooter Footer { get; init; }
    public SmartTextEmbedField[] Fields { get; init; }
    public string PlainText { get; init; }
    public uint Color { get; init; } = 7458112;

    [JsonIgnore]
    public bool IsValid
        => !string.IsNullOrWhiteSpace(Title)
           || !string.IsNullOrWhiteSpace(Description)
           || !string.IsNullOrWhiteSpace(Url)
           || !string.IsNullOrWhiteSpace(Author?.Name)
           || !string.IsNullOrWhiteSpace(Thumbnail)
           || !string.IsNullOrWhiteSpace(Image)
           || (Footer is not null
               && (!string.IsNullOrWhiteSpace(Footer.Text) || !string.IsNullOrWhiteSpace(Footer.IconUrl)))
           || Fields is { Length: > 0 };

    public SmartEmbedTextArray ToArray()
        => new()
        {
            Content = PlainText,
            Embeds =
            [
                new SmartEmbedArrayElementText
                {
                    Title = Title,
                    Description = Description,
                    Url = Url,
                    Thumbnail = Thumbnail,
                    Image = Image,
                    Timestamp = Timestamp,
                    Author = Author,
                    Footer = Footer,
                    Fields = Fields,
                    Color = ColorUintToHex(Color),
                },
            ],
        };

    private static string ColorUintToHex(uint color)
    {
        // Discord color is 24-bit RGB packed as 0x00RRGGBB.
        var r = (byte)((color >> 16) & 0xFF);
        var g = (byte)((color >> 8) & 0xFF);
        var b = (byte)(color & 0xFF);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}

public abstract record SmartEmbedTextBase
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string? Thumbnail { get; init; }
    public string? Image { get; init; }
    public string? Timestamp { get; init; }

    public SmartTextEmbedAuthor? Author { get; init; }
    public SmartTextEmbedFooter? Footer { get; init; }
    public SmartTextEmbedField[]? Fields { get; init; }

    [JsonIgnore]
    public bool IsValid
        => !string.IsNullOrWhiteSpace(Title)
           || !string.IsNullOrWhiteSpace(Description)
           || !string.IsNullOrWhiteSpace(Url)
           || !string.IsNullOrWhiteSpace(Author?.Name)
           || !string.IsNullOrWhiteSpace(Thumbnail)
           || !string.IsNullOrWhiteSpace(Image)
           || (Footer is not null
               && (!string.IsNullOrWhiteSpace(Footer.Text) || !string.IsNullOrWhiteSpace(Footer.IconUrl)))
           || Fields is { Length: > 0 };

    protected SmartEmbedTextBase()
    {
    }

    protected SmartEmbedTextBase(IEmbed eb)
    {
        Title = eb.Title;
        Description = eb.Description;
        Url = eb.Url;
        Thumbnail = eb.Thumbnail?.Url;
        Image = eb.Image?.Url;
        Timestamp = eb.Timestamp?.ToString("o");
        Author = eb.Author is { } ea
            ? new()
            {
                Name = ea.Name,
                Url = ea.Url,
                IconUrl = ea.IconUrl
            }
            : null;
        Footer = eb.Footer is { } ef
            ? new()
            {
                Text = ef.Text,
                IconUrl = ef.IconUrl
            }
            : null;

        if (eb.Fields.Length > 0)
        {
            Fields = eb.Fields.Select(field
                               => new SmartTextEmbedField
                               {
                                   Inline = field.Inline,
                                   Name = field.Name,
                                   Value = field.Value
                               })
                           .ToArray();
        }
    }

    public EmbedBuilder GetEmbed()
        => GetEmbedInternal();

    protected virtual EmbedBuilder GetEmbedInternal()
    {
        var embed = new EmbedBuilder();

        if (!string.IsNullOrWhiteSpace(Title))
            embed.WithTitle(Title);

        if (!string.IsNullOrWhiteSpace(Description))
            embed.WithDescription(Description);

        if (Url is not null && Uri.IsWellFormedUriString(Url, UriKind.Absolute))
            embed.WithUrl(Url);

        if (Footer is not null)
        {
            embed.WithFooter(efb =>
            {
                efb.WithText(Footer.Text);
                if (Uri.IsWellFormedUriString(Footer.IconUrl, UriKind.Absolute))
                    efb.WithIconUrl(Footer.IconUrl);
            });
        }

        if (Thumbnail is not null && Uri.IsWellFormedUriString(Thumbnail, UriKind.Absolute))
            embed.WithThumbnailUrl(Thumbnail);

        if (Image is not null && Uri.IsWellFormedUriString(Image, UriKind.Absolute))
            embed.WithImageUrl(Image);

        if (Author is not null && !string.IsNullOrWhiteSpace(Author.Name))
        {
            if (!Uri.IsWellFormedUriString(Author.IconUrl, UriKind.Absolute))
                Author.IconUrl = null;
            if (!Uri.IsWellFormedUriString(Author.Url, UriKind.Absolute))
                Author.Url = null;

            embed.WithAuthor(Author.Name, Author.IconUrl, Author.Url);
        }

        if (!string.IsNullOrWhiteSpace(Timestamp)
            && DateTimeOffset.TryParse(Timestamp,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var ts))
        {
            embed.WithTimestamp(ts);
        }

        if (Fields is not null)
        {
            foreach (var f in Fields)
            {
                if (!string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.Value))
                    embed.AddField(f.Name, f.Value, f.Inline);
            }
        }

        return embed;
    }

    public void NormalizeFields()
    {
        if (Fields is { Length: > 0 })
        {
            foreach (var f in Fields)
            {
                f.Name = f.Name.TrimTo(256);
                f.Value = f.Value.TrimTo(1024);
            }
        }
    }
}
