using NadekoBot.Modules.Administration.Services;
using System.Text;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class TempVoiceCommands(TempVoiceService tvs) : NadekoModule
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        [BotPerm(GuildPerm.ManageChannels | GuildPerm.ManageRoles | GuildPerm.MoveMembers)]
        public async Task TempVoice([Leftover] IVoiceChannel? channel = null)
        {
            channel ??= ((IGuildUser)ctx.User).VoiceChannel;
            if (channel is null)
            {
                await Response().Error(strs.tempvoice_no_channel).SendAsync();
                return;
            }

            var result = await tvs.ToggleHubAsync(ctx.Guild.Id, channel.Id);

            switch (result)
            {
                case TempVoiceHubResult.Added:
                    await Response().Confirm(strs.tempvoice_added(Format.Bold(channel.Name))).SendAsync();
                    break;
                case TempVoiceHubResult.Removed:
                    await Response().Pending(strs.tempvoice_removed(Format.Bold(channel.Name))).SendAsync();
                    break;
                case TempVoiceHubResult.IsTempChannel:
                    await Response().Error(strs.tempvoice_is_temp).SendAsync();
                    break;
                default:
                    await Response().Error(strs.tempvoice_limit(TempVoiceService.MAX_HUBS)).SendAsync();
                    break;
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        public async Task TempVoiceList()
        {
            var hubs = await tvs.GetHubsAsync(ctx.Guild.Id);

            if (hubs.Count == 0)
            {
                await Response().Confirm(strs.tempvoice_list_none).SendAsync();
                return;
            }

            var sb = new StringBuilder();
            foreach (var hubId in hubs)
                sb.Append("<#").Append(hubId).AppendLine(">");

            var eb = CreateEmbed()
                .WithOkColor()
                .WithTitle(GetText(strs.tempvoice_list))
                .WithDescription(sb.ToString());

            await Response().Embed(eb).SendAsync();
        }
    }
}
