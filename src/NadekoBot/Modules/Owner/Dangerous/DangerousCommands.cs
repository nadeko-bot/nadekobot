#nullable disable
using Microsoft.Data.Sqlite;
using NadekoBot.Modules.Gambling;
using NadekoBot.Modules.Owner.Services;
using NadekoBot.Modules.Xp;
using SQLitePCL;

namespace NadekoBot.Modules.Owner;

public partial class Owner
{
    [Group]
    [OwnerOnly]
    [NoPublicBot]
    [NoAiAgent]
    public partial class DangerousCommands : ConfirmActionModuleBase
    {
        private readonly DangerousCommandsService _ds;
        private readonly IGamblingCleanupService _gcs;
        private readonly IXpCleanupService _xcs;

        public DangerousCommands(
            DangerousCommandsService ds,
            IGamblingCleanupService gcs,
            IXpCleanupService xcs)
        {
            _ds = ds;
            _gcs = gcs;
            _xcs = xcs;
        }

        [Cmd]
        [OwnerOnly]
        public async Task SqlExec([Leftover] string sql)
        {
            try
            {
                var embed = CreateEmbed()
                            .WithTitle(GetText(strs.sql_confirm_exec))
                            .WithDescription(Format.Code(sql));

                if (!await PromptUserConfirmAsync(embed))
                    return;

                var res = await _ds.ExecuteSql(sql);
                await Response().Confirm(res.ToString()).SendAsync();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT)
            {
                await Response()
                      .Error(strs.sql_exec_timeout((int)DangerousCommandsService.ManagedExecTimeLimit.TotalSeconds))
                      .SendAsync();
            }
            catch (Exception ex)
            {
                await Response().Error(ex.ToString()).SendAsync();
            }
        }

        [Cmd]
        [OwnerOnly]
        public Task DeleteXp()
            => ConfirmActionInternalAsync("Delete Xp", () => _xcs.DeleteXp());

        [Cmd]
        [OwnerOnly]
        public Task DeleteCurrency()
            => ConfirmActionInternalAsync("Delete Currency", () => _gcs.DeleteCurrency());
    }
}
