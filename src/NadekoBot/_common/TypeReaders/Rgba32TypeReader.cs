using SixLabors.ImageSharp.PixelFormats;
using Color = SixLabors.ImageSharp.Color;

#nullable disable
namespace NadekoBot.Common.TypeReaders;

public sealed class Rgba32TypeReader : NadekoTypeReader<Rgba32>
{
    public override ValueTask<TypeReaderResult<Rgba32>> ReadAsync(ICommandContext context, string input)
    {
        if (Rgba32.TryParseHex(input, out var clr))
        {
            return ValueTask.FromResult(TypeReaderResult.FromSuccess(clr));
        }

        return ValueTask.FromResult(
            TypeReaderResult.FromError<Rgba32>(CommandError.ParseFailed, "Parameter is not a valid color hex."));
    }
}