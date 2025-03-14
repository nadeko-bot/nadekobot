#nullable disable
using Newtonsoft.Json;
using SixLabors.ImageSharp.PixelFormats;
using Color = SixLabors.ImageSharp.Color;

namespace NadekoBot.Modules.Xp;

public class XpTemplate
{
    public int Version { get; set; } = 3;
    
    [JsonProperty("output_size")]
    public XpTemplatePos OutputSize { get; set; } = new()
    {
        X = 500,
        Y = 245
    };

    public XpTemplateUser User { get; set; } = new()
    {
        Name = new()
        {
            FontSize = 25,
            Show = true,
            Pos = new()
            {
                X = 65,
                Y = 8
            }
        },
        Icon = new()
        {
            Show = true,
            Pos = new()
            {
                X = 11,
                Y = 11
            },
            Size = new()
            {
                X = 38,
                Y = 38
            }
        },
        Level = new()
        {
            Show = true,
            FontSize = 22,
            Pos = new()
            {
                X = 35,
                Y = 101
            }
        },
        Rank = new()
        {
            Show = true,
            FontSize = 20,
            Pos = new()
            {
                X = 100,
                Y = 115
            }
        },
        Xp = new()
        {
            Bar = new()
            {
                Show = true,
                Guild = new()
                {
                    Direction = XpTemplateDirection.Right,
                    Length = 225,
                    Color = new(0, 0, 0, 0.4f),
                    PointA = new()
                    {
                        X = 202,
                        Y = 66
                    },
                    PointB = new()
                    {
                        X = 180,
                        Y = 145
                    }
                }
            },
            Guild = new()
            {
                Show = true,
                FontSize = 25,
                Pos = new()
                {
                    X = 330,
                    Y = 104
                }
            }
        }
    };

    public XpTemplateClub Club { get; set; } = new()
    {
        Icon = new()
        {
            Show = true,
            Pos = new()
            {
                X = 451,
                Y = 15
            },
            Size = new()
            {
                X = 29,
                Y = 29
            }
        },
        Name = new()
        {
            FontSize = 20,
            Pos = new()
            {
                X = 394,
                Y = 40
            },
            Show = true
        }
    };
}

public class XpTemplateIcon
{
    public bool Show { get; set; }
    public XpTemplatePos Pos { get; set; }
    public XpTemplatePos Size { get; set; }
}

public class XpTemplatePos
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class XpTemplateUser
{
    public XpTemplateText Name { get; set; }
    public XpTemplateIcon Icon { get; set; }
    public XpTemplateText Level { get; set; }
    public XpTemplateText Rank { get; set; }
    public XpTemplateXp Xp { get; set; }
}

public class XpTemplateClub
{
    public XpTemplateIcon Icon { get; set; }
    public XpTemplateText Name { get; set; }
}

public class XpTemplateText
{
    [JsonConverter(typeof(XpRgba32Converter))]
    public Rgba32 Color { get; set; } = SixLabors.ImageSharp.Color.White;

    public bool Show { get; set; }
    public int FontSize { get; set; }
    public XpTemplatePos Pos { get; set; }
}

public class XpTemplateXp
{
    public XpTemplateXpBar Bar { get; set; }
    public XpTemplateText Guild { get; set; }
}

public class XpTemplateXpBar
{
    public bool Show { get; set; }
    public XpBar Guild { get; set; }
}

public class XpBar
{
    [JsonConverter(typeof(XpRgba32Converter))]
    public Rgba32 Color { get; set; }

    public XpTemplatePos PointA { get; set; }
    public XpTemplatePos PointB { get; set; }
    public int Length { get; set; }
    public XpTemplateDirection Direction { get; set; }
}

public enum XpTemplateDirection
{
    Up,
    Down,
    Left,
    Right
}

public class XpRgba32Converter : JsonConverter<Rgba32>
{
    public override Rgba32 ReadJson(
        JsonReader reader,
        Type objectType,
        Rgba32 existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
        => Color.ParseHex(reader.Value?.ToString());

    public override void WriteJson(JsonWriter writer, Rgba32 value, JsonSerializer serializer)
        => writer.WriteValue(value.ToHex().ToLowerInvariant());
}