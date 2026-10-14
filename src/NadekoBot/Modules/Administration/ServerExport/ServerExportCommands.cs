using System.Net;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class ServerExportCommands : NadekoModule<ServerExportService>
    {
        private const string CONTACT_URL = "https://discord.nadeko.bot";

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task ServerExport()
        {
            var guild = ctx.Guild;
            if (ctx.User.Id != guild.OwnerId)
            {
                await Response().Error(strs.server_export_owner_only).SendAsync();
                return;
            }

            // An alias, a scheduled command or the AI agent must not use up the owner's export.
            if (ctx.Message is DoAsUserMessage)
            {
                await Response().Error(strs.server_export_self_only).SendAsync();
                return;
            }

            if (await _service.TryReserveAsync(guild.Id) is { } left)
            {
                var next = TimestampTag.FromDateTime(DateTime.UtcNow + left, TimestampTagStyles.Relative);
                await Response().Error(strs.server_export_cooldown(next)).SendAsync();
                return;
            }

            // A cheap DM first, so closed DMs cost no export work.
            try
            {
                await Response().User(ctx.User).Pending(strs.server_export_preparing(guild.Name)).SendAsync();
            }
            catch (Exception ex)
            {
                await _service.ReleaseAsync(guild.Id);
                if (ex is HttpException { DiscordCode: DiscordErrorCode.CannotSendMessageToUser })
                {
                    await Response().Error(strs.server_export_dm_closed).SendAsync();
                    return;
                }

                Log.Warning(ex, "Failed to DM the owner of {GuildId} about a server export", guild.Id);
                await Response().Error(strs.server_export_failed).SendAsync();
                return;
            }

            ServerExportResult result;
            try
            {
                result = await _service.CreateForDmAsync(guild.Id, guild.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Server export of {GuildId} failed", guild.Id);
                await _service.MarkFailedAsync(guild.Id);
                await Response().Error(strs.server_export_failed).SendAsync();
                return;
            }

            switch (result.Status)
            {
                case ServerExportStatus.Busy:
                    await _service.MarkFailedAsync(guild.Id);
                    await Response().Error(strs.server_export_busy).SendAsync();
                    return;
                case ServerExportStatus.TooLarge:
                    await Response().Error(strs.server_export_too_large(CONTACT_URL)).SendAsync();
                    return;
                case ServerExportStatus.NoData:
                    await _service.ReleaseAsync(guild.Id);
                    await Response().Error(strs.server_export_no_data).SendAsync();
                    return;
            }

            using var file = result.File!;
            await SendFileInternalAsync(guild, file);
        }

        private async Task SendFileInternalAsync(IGuild guild, ServerExportFile file)
        {
            var text = GetText(strs.server_export_dm(guild.Name));
            if (!file.XpIncluded)
                text += "\n\n" + GetText(strs.server_export_xp_omitted(CONTACT_URL));

            try
            {
                await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                await Response().User(ctx.User).Confirm(text).File(stream, file.FileName).SendAsync();
            }
            catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
            {
                await _service.ReleaseAsync(guild.Id);
                await Response().Error(strs.server_export_dm_closed).SendAsync();
                return;
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.RequestEntityTooLarge)
            {
                await Response().Error(strs.server_export_too_large(CONTACT_URL)).SendAsync();
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to send the server export of {GuildId}", guild.Id);
                await _service.MarkFailedAsync(guild.Id);
                await Response().Error(strs.server_export_failed).SendAsync();
                return;
            }

            await Response().Confirm(strs.server_export_sent).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        [NoManagedBot]
        public async Task ServerExport(ulong guildId)
        {
            var guildName = (await ctx.Client.GetGuildAsync(guildId))?.Name ?? string.Empty;
            var (status, path) = await _service.SaveFullAsync(guildId, guildName);
            switch (status)
            {
                case ServerExportStatus.Ok:
                    await Response().Confirm(strs.server_export_saved(Format.Code(path!))).SendAsync();
                    return;
                case ServerExportStatus.Busy:
                    await Response().Error(strs.server_export_busy).SendAsync();
                    return;
                default:
                    await Response().Error(strs.server_export_no_data).SendAsync();
                    return;
            }
        }
    }
}
