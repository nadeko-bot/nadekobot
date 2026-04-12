using System.Text.RegularExpressions;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Gambling.Services;
using NCalc;
using OneOf;

namespace NadekoBot.Common.TypeReaders;

public class BaseShmartInputAmountReader
{
    private static readonly Regex _percentRegex = new(@"^((?<num>100|\d{1,2})%)$", RegexOptions.Compiled);
    protected readonly DbService _db;
    protected readonly GamblingConfigService _gambling;

    public BaseShmartInputAmountReader(DbService db, GamblingConfigService gambling)
    {
        _db = db;
        _gambling = gambling;
    }

    public async ValueTask<OneOf<long, OneOf.Types.Error<string>>> ReadAsync(ICommandContext context, string input)
    {
        var i = input.Trim().ToUpperInvariant();

        i = i.Replace("K", "000");

        //can't add m because it will conflict with max atm

        if (await TryHandlePercentage(context, i) is long num)
        {
            return num;
        }

        try
        {
            var expr = new Expression(i, EvaluateOptions.IgnoreCase);

            var needsCur = i.Contains("ALL", StringComparison.Ordinal)
                           || i.Contains("HALF", StringComparison.Ordinal);
            var needsMax = i.Contains("MAX", StringComparison.Ordinal);

            if (needsCur || needsMax)
            {
                var cur = await Cur(context);
                expr.Parameters["ALL"] = (double)cur;
                expr.Parameters["ALLIN"] = (double)cur;
                expr.Parameters["HALF"] = (double)(cur / 2);
            }

            if (needsMax)
            {
                var max = await Max(context);
                expr.Parameters["MAX"] = (double)max;
            }

            expr.EvaluateParameter += static (str, ev) =>
            {
                switch (str.ToUpperInvariant())
                {
                    case "PI":
                        ev.Result = Math.PI;
                        break;
                    case "E":
                        ev.Result = Math.E;
                        break;
                }
            };

            return (long)decimal.Parse(expr.Evaluate().ToString()!);
        }
        catch (Exception)
        {
            return new OneOf.Types.Error<string>($"Invalid input: {input}");
        }
    }

    protected virtual async Task<long> Cur(ICommandContext ctx)
    {
        await using var uow = _db.GetDbContext();
        return await uow.Set<DiscordUser>().GetUserCurrencyAsync(ctx.User.Id);
    }

    protected virtual async Task<long> Max(ICommandContext ctx)
    {
        var settings = _gambling.Data;
        var max = settings.MaxBet;
        return max == 0 ? await Cur(ctx) : max;
    }

    private async Task<long?> TryHandlePercentage(ICommandContext ctx, string input)
    {
        var m = _percentRegex.Match(input);
        
        if (m.Captures.Count == 0)
            return null;
        
        if (!long.TryParse(m.Groups["num"].ToString(), out var percent))
            return null;

        return (long)(await Cur(ctx) * (percent / 100.0f));
    }
}