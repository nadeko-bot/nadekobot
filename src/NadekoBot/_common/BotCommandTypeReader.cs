#nullable disable
using System.Collections.Frozen;

namespace NadekoBot.Common.TypeReaders;

public sealed class CommandTypeReader : NadekoTypeReader<CommandInfo>
{
    private static FrozenDictionary<string, CommandInfo> _lookup;

    private readonly CommandService _cmds;
    private readonly ICommandHandler _handler;

    public CommandTypeReader(ICommandHandler handler, CommandService cmds)
    {
        _handler = handler;
        _cmds = cmds;
    }

    // Medusa adds and removes modules at runtime, and CommandService raises no event for it.
    public static void Invalidate()
        => Volatile.Write(ref _lookup, null);

    public override ValueTask<TypeReaderResult<CommandInfo>> ReadAsync(ICommandContext ctx, string input)
    {
        var prefix = _handler.GetPrefix(ctx.Guild);
        if (!input.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase))
            return new(TypeReaderResult.FromError<CommandInfo>(CommandError.ParseFailed, "No such command found."));

        input = input[prefix.Length..];

        var lookup = Volatile.Read(ref _lookup) ?? Rebuild();

        if (!lookup.TryGetValue(input, out var cmd))
            return new(TypeReaderResult.FromError<CommandInfo>(CommandError.ParseFailed, "No such command found."));

        return new(TypeReaderResult.FromSuccess(cmd));
    }

    private FrozenDictionary<string, CommandInfo> Rebuild()
    {
        var builder = new Dictionary<string, CommandInfo>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var cmd in _cmds.Commands)
        {
            foreach (var alias in cmd.Aliases)
                builder.TryAdd(alias, cmd);
        }

        var lookup = builder.ToFrozenDictionary(StringComparer.InvariantCultureIgnoreCase);
        Volatile.Write(ref _lookup, lookup);
        return lookup;
    }
}
