#nullable disable
using NadekoBot.Common.TypeReaders.Models;

namespace NadekoBot.Common.TypeReaders;

public sealed class StoopidTimeTypeReader : NadekoTypeReader<ParsedTimespan>
{
    public override ValueTask<TypeReaderResult<ParsedTimespan>> ReadAsync(ICommandContext context, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new(TypeReaderResult.FromError<ParsedTimespan>(CommandError.Unsuccessful, "Input is empty."));
        try
        {
            var time = ParsedTimespan.FromInput(input);
            return new(TypeReaderResult.FromSuccess(time));
        }
        catch (Exception ex)
        {
            return new(TypeReaderResult.FromError<ParsedTimespan>(CommandError.Exception, ex.Message));
        }
    }
}