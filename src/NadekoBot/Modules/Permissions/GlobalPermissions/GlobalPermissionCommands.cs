#nullable disable
using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Permissions.Services;

namespace NadekoBot.Modules.Permissions;

public partial class Permissions
{
    [Group]
    public partial class GlobalPermissionCommands : NadekoModule
    {
        private readonly GlobalPermissionService _service;
        private readonly DbService _db;

        public GlobalPermissionCommands(GlobalPermissionService service, DbService db)
        {
            _service = service;
            _db = db;
        }

        [Cmd]
        [OwnerOnly]
        public async Task GlobalPermList()
        {
            var blockedModule = _service.BlockedModules;
            var blockedCommands = _service.BlockedCommands;
            if (!blockedModule.Any() && !blockedCommands.Any())
            {
                await Response().Error(strs.lgp_none).SendAsync();
                return;
            }

            var embed = CreateEmbed().WithOkColor();

            if (blockedModule.Any())
                embed.AddField(GetText(strs.blocked_modules), string.Join("\n", _service.BlockedModules));

            if (blockedCommands.Any())
                embed.AddField(GetText(strs.blocked_commands), string.Join("\n", _service.BlockedCommands));

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task GlobalModule(ModuleOrExpr module)
        {
            var moduleName = module.Name.ToLowerInvariant();

            var added = _service.ToggleModule(moduleName);

            if (added)
            {
                await Response().Confirm(strs.gmod_add(Format.Bold(module.Name))).SendAsync();
                return;
            }

            await Response().Confirm(strs.gmod_remove(Format.Bold(module.Name))).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task GlobalCommand(CommandOrExprInfo cmd)
        {
            var commandName = cmd.Name.ToLowerInvariant();
            var added = _service.ToggleCommand(commandName);

            if (added)
            {
                await Response().Confirm(strs.gcmd_add(Format.Bold(cmd.Name))).SendAsync();
                return;
            }

            await Response().Confirm(strs.gcmd_remove(Format.Bold(cmd.Name))).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task DmModule(ModuleOrExpr module)
        {
            var moduleName = module.Name.ToLowerInvariant();

            var added = _service.ToggleModule(moduleName, true);

            if (added)
            {
                await Response().Confirm(strs.dmmod_add(Format.Bold(module.Name))).SendAsync();
                return;
            }

            await Response().Confirm(strs.dmmod_remove(Format.Bold(module.Name))).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task DmCommand(CommandOrExprInfo cmd)
        {
            var commandName = cmd.Name.ToLowerInvariant();
            var added = _service.ToggleCommand(commandName, true);

            if (added)
            {
                await Response().Confirm(strs.dmcmd_add(Format.Bold(cmd.Name))).SendAsync();
                return;
            }

            await Response().Confirm(strs.dmcmd_remove(Format.Bold(cmd.Name))).SendAsync();
        }
    }
}