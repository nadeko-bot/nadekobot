using NadekoBot.Modules.Utility.Starboard;
using NadekoBot.Modules.Utility.Starboard.Db;
using System.Collections.Frozen;
using System.Text;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class StarboardCommands(DiscordSocketClient client) : NadekoModule<StarboardService>
    {
        private const int PANEL_TIMEOUT_MS = 600_000;
        private const int PROMPT_TIMEOUT_MS = 30_000;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        [BotPerm(ChannelPerm.SendMessages | ChannelPerm.EmbedLinks)]
        public async Task Starboard()
        {
            var guildId = ctx.Guild.Id;
            var sessionId = Guid.NewGuid().ToString("N");

            await _service.SetChannelAsync(guildId, ctx.Channel.Id);

            var thresholdBtn = new ButtonBuilder()
                               .WithCustomId($"starboard:{sessionId}:threshold")
                               .WithLabel(GetText(strs.starboard_btn_threshold))
                               .WithEmote(Emoji.Parse("🔢"))
                               .WithStyle(ButtonStyle.Secondary);

            var emoteBtn = new ButtonBuilder()
                           .WithCustomId($"starboard:{sessionId}:emote")
                           .WithLabel(GetText(strs.starboard_btn_emote))
                           .WithEmote(Emoji.Parse("⭐"))
                           .WithStyle(ButtonStyle.Secondary);

            var limitBtn = new ButtonBuilder()
                           .WithCustomId($"starboard:{sessionId}:limit")
                           .WithLabel(GetText(strs.starboard_btn_limit))
                           .WithEmote(Emoji.Parse("📏"))
                           .WithStyle(ButtonStyle.Secondary);

            var ignoreSelect = new SelectMenuBuilder()
                               .WithCustomId($"starboard:{sessionId}:ignore")
                               .WithType(ComponentType.ChannelSelect)
                               .WithChannelTypes(ChannelType.Text)
                               .WithPlaceholder(GetText(strs.starboard_select_ignored))
                               .WithMinValues(0)
                               .WithMaxValues(StarboardConsts.MAX_IGNORED_CHANNELS);

            var selfStarBtn = new ButtonBuilder()
                              .WithCustomId($"starboard:{sessionId}:selfstar")
                              .WithLabel(GetText(strs.starboard_btn_self_star))
                              .WithStyle(ButtonStyle.Secondary);

            var botsBtn = new ButtonBuilder()
                          .WithCustomId($"starboard:{sessionId}:bots")
                          .WithLabel(GetText(strs.starboard_btn_bots))
                          .WithStyle(ButtonStyle.Secondary);

            var statusBtn = new ButtonBuilder()
                            .WithCustomId($"starboard:{sessionId}:status")
                            .WithLabel(GetText(strs.starboard_btn_status))
                            .WithStyle(ButtonStyle.Secondary);

            var resetBtn = new ButtonBuilder()
                           .WithCustomId($"starboard:{sessionId}:reset")
                           .WithLabel(GetText(strs.starboard_btn_reset))
                           .WithEmote(Emoji.Parse("🗑️"))
                           .WithStyle(ButtonStyle.Danger);

            // Assigned right after the handlers are built; they capture it for the refresh.
            IUserMessage panelMsg = null!;

            var handlers = new List<NadekoInteractionBase>
            {
                CreateSelect(ignoreSelect, OnIgnoreSelectedAsync),
                CreateButton(thresholdBtn, OnThresholdAsync),
                CreateButton(emoteBtn, OnEmoteAsync),
                CreateButton(limitBtn, OnLimitAsync),
                CreateButton(selfStarBtn, OnSelfStarAsync),
                CreateButton(botsBtn, OnBotsAsync),
                CreateButton(statusBtn, OnStatusAsync),
                CreateButton(resetBtn, OnResetAsync)
            };

            panelMsg = await ctx.Channel.SendMessageAsync(
                embed: BuildPanelEmbed(guildId).Build(),
                components: BuildPanelComponents().Build());

            var runTasks = new Task[handlers.Count];
            for (var i = 0; i < handlers.Count; i++)
                runTasks[i] = handlers[i].RunAsync(panelMsg);

            await Task.WhenAll(runTasks);
            await panelMsg.ModifyAsync(m => m.Components = new ComponentBuilder().Build());

            return;

            NadekoInteractionBase CreateButton(ButtonBuilder btn, Func<SocketMessageComponent, Task> onClick)
                => new NadekoButtonInteractionHandler(client,
                    ctx.User.Id,
                    btn,
                    onClick,
                    onlyAuthor: true,
                    singleUse: false,
                    clearAfter: false);

            NadekoInteractionBase CreateSelect(SelectMenuBuilder menu, Func<SocketMessageComponent, Task> onSelect)
                => new NadekoButtonSelectInteractionHandler(client,
                    ctx.User.Id,
                    menu,
                    onSelect,
                    onlyAuthor: true,
                    singleUse: false);

            ComponentBuilder BuildPanelComponents()
            {
                var settings = _service.GetSettings(guildId);
                var notConfigured = settings is null;

                thresholdBtn.WithDisabled(notConfigured);
                emoteBtn.WithDisabled(notConfigured);
                limitBtn.WithDisabled(notConfigured);
                selfStarBtn.WithDisabled(notConfigured);
                botsBtn.WithDisabled(notConfigured);
                statusBtn.WithDisabled(notConfigured);
                resetBtn.WithDisabled(notConfigured);
                ignoreSelect.WithDisabled(notConfigured);

                selfStarBtn.WithStyle(settings?.AllowSelfStar == true ? ButtonStyle.Success : ButtonStyle.Secondary);
                botsBtn.WithStyle(settings?.AllowBots == true ? ButtonStyle.Success : ButtonStyle.Secondary);
                statusBtn.WithStyle(settings is null || settings.IsEnabled
                    ? ButtonStyle.Success
                    : ButtonStyle.Danger);

                var ignored = _service.GetIgnoredChannels(guildId);
                var ignoredDefaults = new SelectMenuDefaultValue[ignored.Count];
                var idx = 0;
                foreach (var channelId in ignored)
                    ignoredDefaults[idx++] = new(channelId, SelectDefaultValueType.Channel);

                ignoreSelect.WithDefaultValues(ignoredDefaults);

                return new ComponentBuilder()
                       .WithSelectMenu(ignoreSelect, 0)
                       .WithButton(thresholdBtn, 1)
                       .WithButton(emoteBtn, 1)
                       .WithButton(limitBtn, 1)
                       .WithButton(selfStarBtn, 2)
                       .WithButton(botsBtn, 2)
                       .WithButton(statusBtn, 2)
                       .WithButton(resetBtn, 3);
            }

            async Task RefreshPanelAsync()
                => await panelMsg.ModifyAsync(m =>
                {
                    m.Embed = BuildPanelEmbed(guildId).Build();
                    m.Components = BuildPanelComponents().Build();
                });

            // The select replaces the whole set, so deselecting everything clears the list.
            async Task OnIgnoreSelectedAsync(SocketMessageComponent smc)
            {
                await smc.DeferAsync();
                await _service.SetIgnoredChannelsAsync(guildId, GetSelectedChannelIds(smc));
                await RefreshPanelAsync();
            }

            async Task OnThresholdAsync(SocketMessageComponent smc)
            {
                if (await AwaitChatInputAsync(smc,
                        strs.starboard_prompt_threshold,
                        static s => int.TryParse(s, out var n)
                                    && n is >= StarboardConsts.MIN_THRESHOLD and <= StarboardConsts.MAX_THRESHOLD)
                    is not { } input)
                    return;

                await _service.SetThresholdAsync(guildId, int.Parse(input));
                await RefreshPanelAsync();
            }

            async Task OnLimitAsync(SocketMessageComponent smc)
            {
                if (await AwaitChatInputAsync(smc,
                        strs.starboard_prompt_limit,
                        static s => int.TryParse(s, out var n)
                                    && n is >= StarboardConsts.MIN_LIMIT and <= StarboardConsts.MAX_LIMIT)
                    is not { } input)
                    return;

                await _service.SetLimitAsync(guildId, int.Parse(input));
                await RefreshPanelAsync();
            }

            async Task OnEmoteAsync(SocketMessageComponent smc)
            {
                if (await AwaitChatInputAsync(smc,
                        strs.starboard_prompt_emote,
                        static s => StarboardService.ParseEmote(s.AsSpan().Trim().ToString()) is not null)
                    is not { } input)
                    return;

                await _service.SetEmoteAsync(guildId, input.AsSpan().Trim().ToString());
                await RefreshPanelAsync();
            }

            async Task OnSelfStarAsync(SocketMessageComponent smc)
            {
                await smc.DeferAsync();
                await _service.ToggleSelfStarAsync(guildId);
                await RefreshPanelAsync();
            }

            async Task OnBotsAsync(SocketMessageComponent smc)
            {
                await smc.DeferAsync();
                await _service.ToggleAllowBotsAsync(guildId);
                await RefreshPanelAsync();
            }

            async Task OnStatusAsync(SocketMessageComponent smc)
            {
                await smc.DeferAsync();
                await _service.ToggleAsync(guildId);
                await RefreshPanelAsync();
            }

            async Task OnResetAsync(SocketMessageComponent smc)
            {
                await smc.DeferAsync();
                await _service.ResetAsync(guildId);
                await RefreshPanelAsync();
            }
        }

        private EmbedBuilder BuildPanelEmbed(ulong guildId)
        {
            var eb = CreateEmbed()
                     .WithOkColor()
                     .WithTitle(GetText(strs.starboard_title));

            var settings = _service.GetSettings(guildId);

            if (settings is null)
                return eb.WithDescription(GetText(strs.starboard_not_set_up));

            eb.AddField(GetText(strs.starboard_channel), $"<#{settings.ChannelId}>", true)
              .AddField(GetText(strs.starboard_emote), settings.EmoteText, true)
              .AddField(GetText(strs.starboard_threshold), settings.Threshold.ToString(), true)
              .AddField(GetText(strs.starboard_limit), settings.Limit.ToString(), true)
              .AddField(GetText(strs.starboard_self_star), settings.AllowSelfStar ? "✅" : "❌", true)
              .AddField(GetText(strs.starboard_bots), settings.AllowBots ? "✅" : "❌", true)
              .AddField(GetText(strs.starboard_status), settings.IsEnabled ? "✅" : "❌", true);

            var ignored = _service.GetIgnoredChannels(guildId);

            if (ignored.Count > 0)
                eb.AddField(GetText(strs.starboard_ignored), FormatChannelMentions(ignored));

            return eb;
        }

        private static string FormatChannelMentions(FrozenSet<ulong> channelIds)
        {
            var sb = new StringBuilder();

            foreach (var channelId in channelIds)
            {
                if (sb.Length > 0)
                    sb.Append(' ');

                sb.Append("<#").Append(channelId).Append('>');
            }

            return sb.ToString();
        }

        // Chat is used instead of a modal, because it keeps the emoji picker available.
        private async Task<string?> AwaitChatInputAsync(
            SocketMessageComponent smc,
            LocStr prompt,
            Func<string, bool> validate)
        {
            await smc.DeferAsync();

            // Not disposed on purpose: a click racing with SetCompleted must not hit a disposed source.
            var cancelSource = new CancellationTokenSource();

            var cancelBtn = new ButtonBuilder()
                            .WithCustomId($"starboard:prompt_cancel:{Guid.NewGuid():N}")
                            .WithLabel(GetText(strs.starboard_btn_cancel))
                            .WithStyle(ButtonStyle.Secondary);

            var cancelInter = new NadekoButtonInteractionHandler(client,
                ctx.User.Id,
                cancelBtn,
                _ =>
                {
                    cancelSource.Cancel();
                    return Task.CompletedTask;
                },
                onlyAuthor: true,
                singleUse: true);

            var promptMsg = await smc.FollowupAsync(GetText(prompt),
                components: new ComponentBuilder().WithButton(cancelBtn).Build(),
                ephemeral: true);

            _ = cancelInter.RunAsync(promptMsg);

            try
            {
                return await GetUserInputAsync(ctx.User.Id,
                    ctx.Channel.Id,
                    validate,
                    PROMPT_TIMEOUT_MS,
                    cancelSource.Token);
            }
            finally
            {
                cancelInter.SetCompleted();

                try { await promptMsg.DeleteAsync(); }
                catch (HttpException) { }
            }
        }

        private static List<ulong> GetSelectedChannelIds(SocketMessageComponent smc)
        {
            var channels = smc.Data.Channels;
            var result = new List<ulong>(channels.Count);

            foreach (var channel in channels)
                result.Add(channel.Id);

            return result;
        }
    }
}
