#nullable disable
using NadekoBot.Common.TypeReaders;
using static NadekoBot.Common.TypeReaders.TypeReaderResult;

namespace NadekoBot.Modules.Permissions;

public class CleverbotResponseCmdCdTypeReader : NadekoTypeReader<CleverBotResponseStr>
{
    public override ValueTask<TypeReaderResult<CleverBotResponseStr>> ReadAsync(
        ICommandContext ctx,
        string input)
        => input.ToLowerInvariant() == CleverBotResponseStr.CLEVERBOT_RESPONSE
            ? new(FromSuccess(new CleverBotResponseStr()))
            : new(FromError<CleverBotResponseStr>(CommandError.ParseFailed, "Not a valid cleverbot"));
}