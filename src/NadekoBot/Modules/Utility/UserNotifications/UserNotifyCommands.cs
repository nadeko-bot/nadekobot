using NadekoBot.Modules.Utility.UserNotifications;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    public class UserNotifyCommands(DiscordSocketClient client) : NadekoModule<UserNotifyService>
    {
        private const int PAGE_SIZE = 8;

        [Cmd]
        public async Task Notify()
        {
            var allEvents = _service.GetAllEvents();
            if (allEvents.Count == 0)
            {
                await Response().Pending(strs.user_notify_none).SendAsync();
                return;
            }

            var blocked = await _service.GetBlockedAsync(ctx.User.Id);
            var userId = ctx.User.Id;
            var totalPages = (allEvents.Count + PAGE_SIZE - 1) / PAGE_SIZE;
            var currentPage = 0;
            var handlers = new List<NadekoButtonInteractionHandler>();

            (EmbedBuilder Embed, ComponentBuilder Components) BuildPanel(
                HashSet<string> blockedKeys,
                int page)
            {
                var eb = CreateEmbed()
                    .WithTitle(GetText(strs.user_notify_title))
                    .WithOkColor();

                var offset = page * PAGE_SIZE;
                var pageItems = allEvents.Skip(offset).Take(PAGE_SIZE).ToArray();

                var sb = new System.Text.StringBuilder();
                for (var i = 0; i < pageItems.Length; i++)
                {
                    var evt = pageItems[i];
                    var enabled = !blockedKeys.Contains(evt.Key);
                    var icon = enabled ? "\u2705" : "\u274C";
                    sb.AppendLine($"{icon} `{offset + i + 1}.` **{GetText(evt.Name)}**");
                }

                if (totalPages > 1)
                    sb.AppendLine().AppendLine($"{page + 1}/{totalPages}");

                eb.WithDescription(sb.ToString())
                  .WithFooter(GetText(strs.user_notify_footer));

                var cb = new ComponentBuilder();
                handlers.Clear();

                for (var i = 0; i < pageItems.Length; i++)
                {
                    var evt = pageItems[i];
                    var enabled = !blockedKeys.Contains(evt.Key);
                    var row = i / 4;
                    var customId = $"unotify:t:{evt.Key}:{Guid.NewGuid():N}";

                    var btn = new ButtonBuilder()
                        .WithCustomId(customId)
                        .WithLabel($"{offset + i + 1}")
                        .WithStyle(enabled ? ButtonStyle.Success : ButtonStyle.Danger);

                    handlers.Add(CreateToggleHandler(userId, evt.Key, btn));
                    cb.WithButton(btn, row);
                }

                var enableAllBtn = new ButtonBuilder()
                    .WithCustomId($"unotify:ea:{Guid.NewGuid():N}")
                    .WithLabel(GetText(strs.user_notify_enable_all))
                    .WithStyle(ButtonStyle.Success);

                handlers.Add(CreateBulkHandler(userId, enableAll: true, enableAllBtn));
                cb.WithButton(enableAllBtn, 2);

                var disableAllBtn = new ButtonBuilder()
                    .WithCustomId($"unotify:da:{Guid.NewGuid():N}")
                    .WithLabel(GetText(strs.user_notify_disable_all))
                    .WithStyle(ButtonStyle.Danger);

                handlers.Add(CreateBulkHandler(userId, enableAll: false, disableAllBtn));
                cb.WithButton(disableAllBtn, 2);

                if (totalPages > 1)
                {
                    var prevBtn = new ButtonBuilder()
                        .WithCustomId($"unotify:prev:{Guid.NewGuid():N}")
                        .WithEmote(new Emoji("\u25C0"))
                        .WithStyle(ButtonStyle.Secondary)
                        .WithDisabled(page == 0);

                    if (page > 0)
                        handlers.Add(CreatePageHandler(userId, -1, prevBtn));

                    cb.WithButton(prevBtn, 2);

                    var nextBtn = new ButtonBuilder()
                        .WithCustomId($"unotify:next:{Guid.NewGuid():N}")
                        .WithEmote(new Emoji("\u25B6"))
                        .WithStyle(ButtonStyle.Secondary)
                        .WithDisabled(page >= totalPages - 1);

                    if (page < totalPages - 1)
                        handlers.Add(CreatePageHandler(userId, 1, nextBtn));

                    cb.WithButton(nextBtn, 2);
                }

                return (eb, cb);
            }

            NadekoButtonInteractionHandler CreateToggleHandler(
                ulong uid,
                string key,
                ButtonBuilder btn)
                => new(client, uid, btn, async smc =>
                {
                    await smc.DeferAsync();
                    await _service.ToggleAsync(uid, key);
                    await RefreshPanelInternalAsync(smc);
                }, onlyAuthor: true, singleUse: false, clearAfter: false);

            NadekoButtonInteractionHandler CreateBulkHandler(
                ulong uid,
                bool enableAll,
                ButtonBuilder btn)
                => new(client, uid, btn, async smc =>
                {
                    await smc.DeferAsync();
                    if (enableAll)
                        await _service.EnableAllAsync(uid);
                    else
                        await _service.DisableAllAsync(uid);
                    await RefreshPanelInternalAsync(smc);
                }, onlyAuthor: true, singleUse: false, clearAfter: false);

            NadekoButtonInteractionHandler CreatePageHandler(
                ulong uid,
                int delta,
                ButtonBuilder btn)
                => new(client, uid, btn, async smc =>
                {
                    await smc.DeferAsync();
                    currentPage = Math.Clamp(currentPage + delta, 0, totalPages - 1);
                    await RefreshPanelInternalAsync(smc);
                }, onlyAuthor: true, singleUse: false, clearAfter: false);

            async Task RefreshPanelInternalAsync(SocketMessageComponent smc)
            {
                foreach (var h in handlers)
                    h.SetCompleted();

                var refreshed = await _service.GetBlockedAsync(userId);
                var (newEmbed, newComponents) = BuildPanel(refreshed, currentPage);

                await smc.Message.ModifyAsync(m =>
                {
                    m.Embed = newEmbed.Build();
                    m.Components = newComponents.Build();
                });

                var msg = smc.Message;
                var runTasks = new Task[handlers.Count];
                for (var j = 0; j < handlers.Count; j++)
                    runTasks[j] = handlers[j].RunAsync(msg);
                await Task.WhenAll(runTasks);
            }

            var (embed, components) = BuildPanel(blocked, currentPage);

            var msg = await ctx.Channel.SendMessageAsync(
                embed: embed.Build(),
                components: components.Build());

            var tasks = new Task[handlers.Count];
            for (var i = 0; i < handlers.Count; i++)
                tasks[i] = handlers[i].RunAsync(msg);

            await Task.WhenAll(tasks);
            await msg.ModifyAsync(m => m.Components = new ComponentBuilder().Build());
        }
    }
}
