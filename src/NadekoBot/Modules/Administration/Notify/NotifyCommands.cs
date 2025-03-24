using NadekoBot.Db.Models;
using System.Text;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    public class NotifyCommands : NadekoModule<NotifyService>
    {
        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task Notify()
        {
            await Response()
                .Paginated()
                .Items(Enum.GetValues<NotifyType>().DistinctBy(x => (int)x).ToList())
                .PageSize(5)
                .Page((items, page) =>
                {
                    var eb = CreateEmbed()
                        .WithOkColor()
                        .WithTitle(GetText(strs.notify_available));

                    foreach (var item in items)
                    {
                        eb.AddField(item.ToString(), GetText(GetDescription(item)), false);
                    }

                    return eb;
                })
                .SendAsync();
        }

        private LocStr GetDescription(NotifyType item)
            => item switch
            {
                NotifyType.LevelUp => strs.notify_desc_levelup,
                NotifyType.Protection => strs.notify_desc_protection,
                NotifyType.AddRoleReward => strs.notify_desc_addrolerew,
                NotifyType.RemoveRoleReward => strs.notify_desc_removerolerew,
                NotifyType.NiceCatch => strs.notify_desc_nicecatch,
                _ => strs.notify_desc_not_found
            };

        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task Notify(NotifyType nType)
        {
            // show msg 
            var conf = await _service.GetNotifyAsync(ctx.Guild.Id, nType);
            if (conf is null)
            {
                await Response().Confirm(strs.notify_msg_not_set).SendAsync();
                return;
            }

            var outChannel = conf.ChannelId is null
                ? """
                  from which the event originated
                  `origin`
                  """
                : $"""
                   <#{conf.ChannelId}>
                   `{conf.ChannelId}`
                   """;
            var eb = CreateEmbed()
                .WithOkColor()
                .WithTitle(GetText(strs.notify_msg))
                .WithDescription(conf.Message.TrimTo(2048))
                .AddField(GetText(strs.notify_type), conf.Type.ToString(), true)
                .AddField(GetText(strs.channel),
                    outChannel,
                    true);

            await Response().Embed(eb).SendAsync();
            return;
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task Notify(NotifyType nType, [Leftover] string message)
            => await NotifyInternalAsync(nType, null, message);

        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task Notify(NotifyType nType, IMessageChannel channel, [Leftover] string message)
            => await NotifyInternalAsync(nType, channel, message);

        private async Task NotifyInternalAsync(NotifyType nType, IMessageChannel? channel, [Leftover] string message)
        {
            var result = await _service.EnableAsync(ctx.Guild.Id, channel?.Id, nType, message);

            if(!result)
            {
                await Response()
                    .Error(strs.notify_cant_set)
                    .SendAsync();
                
                return;
            }
            var outChannel = channel is null ? "origin" : $"<#{channel.Id}>";
            await Response()
                .Confirm(strs.notify_on(outChannel, Format.Bold(nType.ToString())))
                .SendAsync();
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task NotifyPhs(NotifyType nType)
        {
            var data = _service.GetRegisteredModel(nType);

            var eb = CreateEmbed()
                .WithOkColor()
                .WithTitle(GetText(strs.notify_placeholders(nType.ToString().ToLower())));

            eb.WithDescription(data.Replacements.Join("\n---\n", x => $"`%event.{x}%`"));

            await Response().Embed(eb).SendAsync();
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task NotifyList(int page = 1)
        {
            if (--page < 0)
                return;

            var notifs = await _service.GetForGuildAsync(ctx.Guild.Id);

            var sb = new StringBuilder();

            foreach (var notif in notifs)
            {
                sb.AppendLine($"""
                               - **{notif.Type}**  
                                 <#{notif.ChannelId}> `{notif.ChannelId}`

                               """);
            }

            if (notifs.Count == 0)
                sb.AppendLine(GetText(strs.notify_none));

            await Response()
                .Confirm(GetText(strs.notify_list), text: sb.ToString())
                .SendAsync();
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task NotifyClear(NotifyType nType)
        {
            await _service.DisableAsync(ctx.Guild.Id, nType);
            await Response().Confirm(strs.notify_off(nType)).SendAsync();
        }
    }
}