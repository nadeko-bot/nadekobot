#nullable disable
using NadekoBot.Modules.NadekoExpressions;

namespace NadekoBot.Common.TypeReaders;

public sealed class CommandOrExprTypeReader : NadekoTypeReader<CommandOrExprInfo>
{
    private readonly CommandService _cmds;
    private readonly ICommandHandler _commandHandler;
    private readonly NadekoExpressionsService _exprs;

    public CommandOrExprTypeReader(CommandService cmds, NadekoExpressionsService exprs, ICommandHandler commandHandler)
    {
        _cmds = cmds;
        _exprs = exprs;
        _commandHandler = commandHandler;
    }

    public override async ValueTask<TypeReaderResult<CommandOrExprInfo>> ReadAsync(ICommandContext ctx, string input)
    {
        if (_exprs.ExpressionExists(ctx.Guild?.Id, input))
            return TypeReaderResult.FromSuccess(new CommandOrExprInfo(input, CommandOrExprInfo.Type.Custom));

        var cmd = await new CommandTypeReader(_commandHandler, _cmds).ReadAsync(ctx, input);
        if (cmd.IsSuccess)
        {
            return TypeReaderResult.FromSuccess(new CommandOrExprInfo(((CommandInfo)cmd.Values.First().Value).Name,
                CommandOrExprInfo.Type.Normal));
        }

        return TypeReaderResult.FromError<CommandOrExprInfo>(CommandError.ParseFailed, "No such command or expression found.");
    }
}