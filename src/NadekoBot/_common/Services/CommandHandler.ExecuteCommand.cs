using System.Buffers;
using System.Runtime.CompilerServices;
using ExecuteResult = Discord.Commands.ExecuteResult;
using PreconditionResult = Discord.Commands.PreconditionResult;

namespace NadekoBot.Services;

public sealed partial class CommandHandler
{
    private struct Candidate
    {
        public CommandMatch Match;
        public PreconditionResult Precondition;
        public ParseResult Parse;
        public float Score;
        public bool PreconditionOk;
        public bool ParseOk;
    }

    public Task<(bool Success, string? Error, CommandInfo? Info)> ExecuteCommandAsync(
        ICommandContext context,
        string input,
        int argPos,
        IServiceProvider serviceProvider,
        MultiMatchHandling multiMatchHandling = MultiMatchHandling.Exception)
        => ExecuteCommand(context, input[argPos..], serviceProvider, multiMatchHandling);

    public async Task<(bool Success, string? Error, CommandInfo? Info)> ExecuteCommand(
        ICommandContext context,
        string input,
        IServiceProvider services,
        MultiMatchHandling multiMatchHandling = MultiMatchHandling.Exception)
    {
        var searchResult = _commandService.Search(context, input);
        if (!searchResult.IsSuccess)
            return (false, null, null);

        var commands = searchResult.Commands;
        var count = commands.Count;

        var candidates = ArrayPool<Candidate>.Shared.Rent(count);
        try
        {
            // 1) Check preconditions (single pass)
            var successCount = 0;
            for (var i = 0; i < count; i++)
            {
                var match = commands[i];
                var precondition = await match.Command.CheckPreconditionsAsync(context, services);
                candidates[i] = new Candidate
                {
                    Match = match,
                    Precondition = precondition,
                    PreconditionOk = precondition.IsSuccess
                };

                if (precondition.IsSuccess)
                    successCount++;
            }

            // 2) All preconditions failed -> pick highest-priority failure
            if (successCount == 0)
            {
                var bestIdx = 0;
                var bestPriority = candidates[0].Match.Command.Priority;
                for (var i = 1; i < count; i++)
                {
                    var priority = candidates[i].Match.Command.Priority;
                    if (priority > bestPriority)
                    {
                        bestPriority = priority;
                        bestIdx = i;
                    }
                }

                return (false, candidates[bestIdx].Precondition.ErrorReason, commands[0].Command);
            }

            // 3) Parse successful preconditions + compute score once per entry
            var successParseCount = 0;
            for (var i = 0; i < count; i++)
            {
                if (!candidates[i].PreconditionOk)
                    continue;

                var match = candidates[i].Match;
                var precondition = candidates[i].Precondition;
                var parseResult = await match.ParseAsync(context, searchResult, precondition, services);

                if (parseResult.Error == CommandError.MultipleMatches
                    && multiMatchHandling == MultiMatchHandling.Best)
                {
                    var argList = parseResult.ArgValues.Map(x => MaxByScore(x.Values));
                    var paramList = parseResult.ParamValues.Map(x => MaxByScore(x.Values));
                    parseResult = ParseResult.FromSuccess(argList, paramList);
                }

                candidates[i].Parse = parseResult;
                candidates[i].ParseOk = parseResult.IsSuccess;
                candidates[i].Score = CalculateScoreInternal(in match, in parseResult);

                if (parseResult.IsSuccess)
                    successParseCount++;
            }

            // 4) All parses failed -> pick highest-scoring failure
            if (successParseCount == 0)
            {
                var bestIdx = -1;
                var bestScore = float.NegativeInfinity;
                for (var i = 0; i < count; i++)
                {
                    ref var c = ref candidates[i];
                    if (c.PreconditionOk && !c.ParseOk && c.Score > bestScore)
                    {
                        bestScore = c.Score;
                        bestIdx = i;
                    }
                }

                return (false,
                    bestIdx >= 0 ? candidates[bestIdx].Parse.ErrorReason : null,
                    commands[0].Command);
            }

            // 5) Pick best successful parse (max score)
            var winIdx = -1;
            var winScore = float.NegativeInfinity;
            for (var i = 0; i < count; i++)
            {
                ref var c = ref candidates[i];
                if (c.ParseOk && c.Score > winScore)
                {
                    winScore = c.Score;
                    winIdx = i;
                }
            }

            var winner = candidates[winIdx];
            var cmd = winner.Match.Command;

            var intercepted = await _behaviorHandler.RunPreCommandAsync(context, cmd);
            if (intercepted)
                return (false, null, cmd);

            var execResult = (ExecuteResult)await winner.Match.ExecuteAsync(context, winner.Parse, services);

            if (execResult.Exception is not null
                && (execResult.Exception is not HttpException he
                    || he.DiscordCode != DiscordErrorCode.InsufficientPermissions))
                Log.Warning(execResult.Exception, "Command Error");

            return (true, null, cmd);
        }
        finally
        {
            ArrayPool<Candidate>.Shared.Return(candidates, clearArray: true);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateScoreInternal(in CommandMatch match, in ParseResult parseResult)
    {
        float argValuesScore = 0, paramValuesScore = 0;

        if (match.Command.Parameters.Count > 0)
        {
            var argSum = SumOfMaxScoresInternal(parseResult.ArgValues);
            var paramSum = SumOfMaxScoresInternal(parseResult.ParamValues);
            argValuesScore = argSum / match.Command.Parameters.Count;
            paramValuesScore = paramSum / match.Command.Parameters.Count;
        }

        var totalArgsScore = (argValuesScore + paramValuesScore) / 2f;
        return match.Command.Priority + (totalArgsScore * 0.99f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SumOfMaxScoresInternal(IReadOnlyList<Discord.Commands.TypeReaderResult>? list)
    {
        if (list is null)
            return 0f;

        var sum = 0f;
        for (var i = 0; i < list.Count; i++)
            sum += MaxScoreInternal(list[i].Values);

        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float MaxScoreInternal(IReadOnlyCollection<TypeReaderValue>? values)
    {
        if (values is null)
            return 0f;

        var max = 0f;
        foreach (var v in values)
        {
            if (v.Score > max)
                max = v.Score;
        }

        return max;
    }

    private static TypeReaderValue MaxByScore(IReadOnlyCollection<TypeReaderValue> values)
    {
        var best = default(TypeReaderValue);
        var bestScore = float.NegativeInfinity;
        foreach (var v in values)
        {
            if (v.Score > bestScore)
            {
                bestScore = v.Score;
                best = v;
            }
        }

        return best;
    }
}
