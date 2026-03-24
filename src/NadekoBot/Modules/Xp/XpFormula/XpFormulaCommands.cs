using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.Modules.Xp;

public partial class Xp
{
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public class XpFormulaCommands(XpConfigService xcs) : NadekoModule<XpFormulaService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpFormula()
        {
            var f = _service.GetFormula(ctx.Guild.Id);
            var (a, c) = (f.A, f.C);
            var xpPerMsg = xcs.Data.TextXpPerMessage;

            var eb = CreateEmbed()
                .WithOkColor()
                .WithTitle(GetText(strs.xpformula_current))
                .WithDescription(GetText(strs.xpformula_desc(a, c)))
                .AddField(GetText(strs.xpformula_preview),
                    BuildPreviewTable(a, c, xpPerMsg));

            await Response().Embed(eb).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpFormula(int a, int c)
        {
            if (a < XpFormulaService.MIN_A || a > XpFormulaService.MAX_A)
            {
                await Response()
                    .Error(strs.xpformula_a_invalid(XpFormulaService.MIN_A, XpFormulaService.MAX_A))
                    .SendAsync();
                return;
            }

            if (c < XpFormulaService.MIN_C || c > XpFormulaService.MAX_C)
            {
                await Response()
                    .Error(strs.xpformula_c_invalid(XpFormulaService.MIN_C, XpFormulaService.MAX_C))
                    .SendAsync();
                return;
            }

            var success = await _service.SetFormulaAsync(ctx.Guild.Id, a, c);
            if (!success)
            {
                await Response().Error(strs.xpformula_invalid).SendAsync();
                return;
            }

            var xpPerMsg = xcs.Data.TextXpPerMessage;

            var eb = CreateEmbed()
                .WithOkColor()
                .WithTitle(GetText(strs.xpformula_set))
                .WithDescription(GetText(strs.xpformula_desc(a, c)))
                .AddField(GetText(strs.xpformula_preview),
                    BuildPreviewTable(a, c, xpPerMsg));

            await Response().Embed(eb).SendAsync();
        }

        private static string BuildPreviewTable(int a, int c, int xpPerMsg)
        {
            int[] levels = [1, 5, 10, 50, 100];
            var lines = new List<string>();

            foreach (var lvl in levels)
            {
                var totalXp = LevelStats.GetTotalXpReqForLevel(lvl, a, c);
                var msgs = xpPerMsg > 0
                    ? (long)Math.Ceiling((double)totalXp / xpPerMsg)
                    : -1;

                lines.Add(msgs >= 0
                    ? $"`Lvl {lvl,3}` - **{totalXp:N0}** xp ({msgs:N0} msgs)"
                    : $"`Lvl {lvl,3}` - **{totalXp:N0}** xp");
            }

            return string.Join('\n', lines);
        }
    }
}
