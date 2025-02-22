using NadekoBot.Common.TypeReaders;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Administration.Services;

public sealed class EmoteTypeReader : NadekoTypeReader<IEmote>
{
    public static bool TryParse(string input, [NotNullWhen(true)] out IEmote? emote)
    {
        emote = null;

        if (Emoji.TryParse(input, out var emoji))
            emote = emoji;
        else if (Emote.TryParse(input, out var emote2))
            emote = emote2;

        return emote is not null;
    }

    public override ValueTask<TypeReaderResult<IEmote>> ReadAsync(ICommandContext ctx, string input)
    {
        if (TryParse(input, out var emote))
            return ValueTask.FromResult<TypeReaderResult<IEmote>>(Discord.Commands.TypeReaderResult.FromSuccess(emote));

        return ValueTask.FromResult<TypeReaderResult<IEmote>>(Discord.Commands.TypeReaderResult.FromError(CommandError.ParseFailed, "Invalid emote"));
    }
}