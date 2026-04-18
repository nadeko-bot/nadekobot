namespace NadekoBot.Common;

public sealed class ReplacementService : IReplacementService, INService
{
    private readonly IReplacementPatternStore _repReg;

    public ReplacementService(IReplacementPatternStore repReg)
    {
        _repReg = repReg;
    }

    public async ValueTask<SmartText> ReplaceAsync(SmartText input, ReplacementContext repCtx)
    {
        var rep = CreateReplacer(repCtx);
        return await rep.ReplaceAsync(input);
    }

    public async ValueTask<string?> ReplaceAsync(string input, ReplacementContext repCtx)
    {
        var rep = CreateReplacer(repCtx);
        return await rep.ReplaceAsync(input);
    }

    private Replacer CreateReplacer(ReplacementContext repCtx)
    {
        var mask = ComputeMask(repCtx);
        var baseReps = _repReg.GetReplacementsForMask(mask);
        var baseRegex = _repReg.GetRegexReplacementsForMask(mask);
        var inputData = BuildInputData(repCtx);

        return new Replacer(baseReps, baseRegex, repCtx.Overrides, repCtx.RegexOverrides, inputData);
    }

    private static ContextMask ComputeMask(ReplacementContext ctx)
    {
        var m = ContextMask.None;
        if (ctx.Client is not null) m |= ContextMask.Client;
        if (ctx.Guild is not null) m |= ContextMask.Guild;
        if (ctx.Channel is not null) m |= ContextMask.Channel;
        if (ctx.User is not null) m |= ContextMask.User;
        return m;
    }

    private static object?[] BuildInputData(ReplacementContext ctx)
        => [ctx.Client, ctx.Guild, ctx.Channel, ctx.User];
}