using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Common.JsonConverters;

public class Rgba32Converter : JsonConverter<Rgba32>
{
    public override Rgba32 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Rgba32.ParseHex(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, Rgba32 value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToHex());
}