#nullable disable
using NadekoBot.Modules.Permissions;

namespace NadekoBot.Common.TypeReaders;

public sealed class ModuleTypeReader : NadekoTypeReader<ModuleInfo>
{
    private readonly CommandService _cmds;

    public ModuleTypeReader(CommandService cmds)
        => _cmds = cmds;

    public override ValueTask<TypeReaderResult<ModuleInfo>> ReadAsync(ICommandContext context, string input)
    {
        input = input.ToUpperInvariant();
        var module = _cmds.Modules.GroupBy(m => m.GetTopLevelModule())
                          .FirstOrDefault(m => m.Key.Name.ToUpperInvariant() == input)
                          ?.Key;
        if (module is null)
            return new(TypeReaderResult.FromError<ModuleInfo>(CommandError.ParseFailed, "No such module found."));

        return new(TypeReaderResult.FromSuccess(module));
    }
}

public sealed class ModuleOrExprTypeReader : NadekoTypeReader<ModuleOrExpr>
{
    private readonly CommandService _cmds;

    public ModuleOrExprTypeReader(CommandService cmds)
        => _cmds = cmds;

    public override ValueTask<TypeReaderResult<ModuleOrExpr>> ReadAsync(ICommandContext context, string input)
    {
        input = input.ToUpperInvariant();
        var module = _cmds.Modules.GroupBy(m => m.GetTopLevelModule())
                          .FirstOrDefault(m => m.Key.Name.ToUpperInvariant() == input)
                          ?.Key;
        if (module is null && input != "ACTUALEXPRESSIONS" && input != CleverBotResponseStr.CLEVERBOT_RESPONSE)
            return new(TypeReaderResult.FromError<ModuleOrExpr>(CommandError.ParseFailed, "No such module found."));

        return new(TypeReaderResult.FromSuccess(new ModuleOrExpr
        {
            Name = input
        }));
    }
}

public sealed class ModuleOrExpr
{
    public string Name { get; set; }
}