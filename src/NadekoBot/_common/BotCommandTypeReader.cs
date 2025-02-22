#nullable disable
namespace NadekoBot.Common.TypeReaders;

public sealed class CommandTypeReader : NadekoTypeReader<CommandInfo>
{
    private readonly CommandService _cmds;
    private readonly ICommandHandler _handler;

    public CommandTypeReader(ICommandHandler handler, CommandService cmds)
    {
        _handler = handler;
        _cmds = cmds;
    }

    public override ValueTask<TypeReaderResult<CommandInfo>> ReadAsync(ICommandContext ctx, string input)
    {
        input = input.ToUpperInvariant();
        var prefix = _handler.GetPrefix(ctx.Guild);
        if (!input.StartsWith(prefix.ToUpperInvariant(), StringComparison.InvariantCulture))
            return new(TypeReaderResult.FromError<CommandInfo>(CommandError.ParseFailed, "No such command found."));

        input = input[prefix.Length..];

        var cmd = _cmds.Commands.FirstOrDefault(c => c.Aliases.Select(a => a.ToUpperInvariant()).Contains(input));
        if (cmd is null)
            return new(TypeReaderResult.FromError<CommandInfo>(CommandError.ParseFailed, "No such command found."));

        return new(TypeReaderResult.FromSuccess(cmd));
    }
}