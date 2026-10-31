#nullable disable
using NCalc;
using System.Reflection;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class CalcCommands : NadekoModule
    {
        [Cmd]
        public async Task Calculate([Leftover] string expression)
        {
            var expr = new Expression(expression, EvaluateOptions.IgnoreCase | EvaluateOptions.NoCache);
            expr.EvaluateParameter += Expr_EvaluateParameter;

            object result;
            try
            {
                if (expr.HasErrors())
                {
                    await Response().Error("⚙ " + GetText(strs.error), expr.Error).SendAsync();
                    return;
                }

                result = expr.Evaluate();
            }
            // NCalc names the unknown variable or function in ParamName; a wrong argument count has none
            catch (ArgumentException ex) when (ex.ParamName is not null)
            {
                await Response().Error("⚙ " + GetText(strs.error), GetText(strs.calc_no_result)).SendAsync();
                return;
            }
            catch (Exception ex)
            {
                await Response().Error("⚙ " + GetText(strs.error), ex.Message).SendAsync();
                return;
            }

            if (result is null)
            {
                await Response().Error("⚙ " + GetText(strs.error), GetText(strs.calc_no_result)).SendAsync();
                return;
            }

            await Response().Confirm("⚙ " + GetText(strs.result), result.ToString()).SendAsync();
        }

        private static void Expr_EvaluateParameter(string name, ParameterArgs args)
        {
            if (name.Equals("pi", StringComparison.OrdinalIgnoreCase))
                args.Result = Math.PI;
            else if (name.Equals("e", StringComparison.OrdinalIgnoreCase))
                args.Result = Math.E;
        }

        [Cmd]
        public async Task CalcOps()
        {
            var selection = typeof(Math).GetTypeInfo()
                                        .GetMethods()
                                        .DistinctBy(x => x.Name)
                                        .Select(x => x.Name)
                                        .Except(new[] { "ToString", "Equals", "GetHashCode", "GetType" });
            await Response().Confirm(GetText(strs.calcops(prefix)), string.Join(", ", selection)).SendAsync();
        }
    }
}