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
        var reps = GetReplacementsForContext(repCtx);
        var regReps = GetRegexReplacementsForContext(repCtx);

        var inputData = GetInputData(repCtx);
        var rep = new Replacer(reps.Values, regReps.Values, inputData);

        return await rep.ReplaceAsync(input);
    }

    public async ValueTask<string?> ReplaceAsync(string input, ReplacementContext repCtx)
    {
        var reps = GetReplacementsForContext(repCtx);
        var regReps = GetRegexReplacementsForContext(repCtx);

        var inputData = GetInputData(repCtx);
        var rep = new Replacer(reps.Values, regReps.Values, inputData);

        return await rep.ReplaceAsync(input);
    }

    private object[] GetInputData(ReplacementContext repCtx)
    {
        var obj = new List<object>();
        if (repCtx.Client is not null)
            obj.Add(repCtx.Client);

        if (repCtx.Guild is not null)
            obj.Add(repCtx.Guild);

        if (repCtx.User is not null)
            obj.Add(repCtx.User);

        if (repCtx.Channel is not null)
            obj.Add(repCtx.Channel);

        return obj.ToArray();
    }

    private IDictionary<string, ReplacementInfo> GetReplacementsForContext(ReplacementContext repCtx)
    {
        var reps = GetOriginalReplacementsForContext(repCtx);
        foreach (var ovrd in repCtx.Overrides)
        {
            reps.Remove(ovrd.Token);
            reps.TryAdd(ovrd.Token, ovrd);
        }

        return reps;
    }

    private IDictionary<string, RegexReplacementInfo> GetRegexReplacementsForContext(ReplacementContext repCtx)
    {
        var reps = GetOriginalRegexReplacementsForContext(repCtx);
        foreach (var ovrd in repCtx.RegexOverrides)
        {
            reps.Remove(ovrd.Pattern);
            reps.TryAdd(ovrd.Pattern, ovrd);
        }

        return reps;
    }

    private IDictionary<string, ReplacementInfo> GetOriginalReplacementsForContext(ReplacementContext repCtx)
    {
        var objs = new List<object>();
        if (repCtx.Client is not null)
        {
            objs.Add(repCtx.Client);
        }

        if (repCtx.Channel is not null)
        {
            objs.Add(repCtx.Channel);
        }

        if (repCtx.User is not null)
        {
            objs.Add(repCtx.User);
        }

        if (repCtx.Guild is not null)
        {
            objs.Add(repCtx.Guild);
        }

        var types = objs.Map(x => x.GetType()).OrderBy(x => x.Name).ToHashSet();

        return _repReg.Replacements
            .Values
            .Where(rep => rep.InputTypes.All(t => types.Any(x => x.IsAssignableTo((t)))))
            .ToDictionary(rep => rep.Token, rep => rep);
    }

    private IDictionary<string, RegexReplacementInfo> GetOriginalRegexReplacementsForContext(ReplacementContext repCtx)
    {
        var objs = new List<object>();
        if (repCtx.Client is not null)
        {
            objs.Add(repCtx.Client);
        }

        if (repCtx.Channel is not null)
        {
            objs.Add(repCtx.Channel);
        }

        if (repCtx.User is not null)
        {
            objs.Add(repCtx.User);
        }

        if (repCtx.Guild is not null)
        {
            objs.Add(repCtx.Guild);
        }

        var types = objs.Map(x => x.GetType()).OrderBy(x => x.Name).ToHashSet();

        return _repReg.RegexReplacements
            .Values
            .Where(rep => rep.InputTypes.All(t => types.Any(x => x.IsAssignableTo((t)))))
            .ToDictionary(rep => rep.Pattern, rep => rep);
    }
}