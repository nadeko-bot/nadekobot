using NadekoBot.Common.JsonConverters;
using System.Text.Json;

namespace NadekoBot.Common;

public class JsonSeria : ISeria
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        IncludeFields = true,
        Converters =
        {
            new Rgba32Converter(),
            new CultureInfoConverter()
        }
    };

    public byte[] Serialize<T>(T data)
        => JsonSerializer.SerializeToUtf8Bytes(data, _serializerOptions);

    public T? Deserialize<T>(byte[]? data)
    {
        if (data is null)
            return default;

        return JsonSerializer.Deserialize<T>(data, _serializerOptions);
    }
}