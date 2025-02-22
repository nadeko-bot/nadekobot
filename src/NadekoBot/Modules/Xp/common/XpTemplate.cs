#nullable disable
using Newtonsoft.Json;
using SixLabors.ImageSharp.PixelFormats;
using Color = SixLabors.ImageSharp.Color;

namespace NadekoBot.Modules.Xp;

public class XpTemplate
{
    public int Version { get; set; } = 2;
    
    [JsonProperty("output_size")]
    public XpTemplatePos OutputSize { get; set; } = new()
    {
        X = 800,
        Y = 392
    };

    public XpTemplateUser User { get; set; } = new()
    {
        Name = new()
        {
            FontSize = 50,
            Show = true,
            Pos = new()
            {
                X = 130,
                Y = 17
            }
        },
        Icon = new()
        {
            Show = true,
            Pos = new()
            {
                X = 14,
                Y = 14
            },
            Size = new()
            {
                X = 72,
                Y = 71
            }
        },
        GuildLevel = new()
        {
            Show = true,
            FontSize = 45,
            Pos = new()
            {
                X = 47,
                Y = 308
            }
        },
        GlobalLevel = new()
        {
            Show = true,
            FontSize = 45,
            Pos = new()
            {
                X = 47,
                Y = 160
            }
        },
        GuildRank = new()
        {
            Show = true,
            FontSize = 30,
            Pos = new()
            {
                X = 148,
                Y = 326
            }
        },
        GlobalRank = new()
        {
            Show = true,
            FontSize = 30,
            Pos = new()
            {
                X = 148,
                Y = 179
            }
        },
        Xp = new()
        {
            Bar = new()
            {
                Show = true,
                Global = new()
                {
                    Direction = XpTemplateDirection.Right,
                    Length = 450,
                    Color = new(0, 0, 0, 0.4f),
                    PointA = new()
                    {
                        X = 321,
                        Y = 104
                    },
                    PointB = new()
                    {
                        X = 286,
                        Y = 235
                    }
                },
                Guild = new()
                {
                    Direction = XpTemplateDirection.Right,
                    Length = 450,
                    Color = new(0, 0, 0, 0.4f),
                    PointA = new()
                    {
                        X = 282,
                        Y = 248
                    },
                    PointB = new()
                    {
                        X = 247,
                        Y = 379
                    }
                }
            },
            Global = new()
            {
                Show = true,
                FontSize = 50,
                Pos = new()
                {
                    X = 528,
                    Y = 170
                }
            },
            Guild = new()
            {
                Show = true,
                FontSize = 50,
                Pos = new()
                {
                    X = 490,
                    Y = 313
                }
            },
            Awarded = new()
            {
                Show = true,
                FontSize = 25,
                Pos = new()
                {
                    X = 490,
                    Y = 345
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
                X = 722,
                Y = 25
            },
            Size = new()
            {
                X = 45,
                Y = 45
            }
        },
        Name = new()
        {
            FontSize = 35,
            Pos = new()
            {
                X = 650,
                Y = 49
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
    public XpTemplateText GlobalLevel { get; set; }
    public XpTemplateText GuildLevel { get; set; }
    public XpTemplateText GlobalRank { get; set; }
    public XpTemplateText GuildRank { get; set; }
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
    public XpTemplateText Global { get; set; }
    public XpTemplateText Guild { get; set; }
    public XpTemplateText Awarded { get; set; }
}

public class XpTemplateXpBar
{
    public bool Show { get; set; }
    public XpBar Global { get; set; }
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