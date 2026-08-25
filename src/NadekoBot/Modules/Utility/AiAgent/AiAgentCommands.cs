using System.Text;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Utility.AiAgent;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class AiAgentCommands(
        DiscordSocketClient client,
        AiAgentWhitelistService whitelist) : NadekoModule<AiAgent.AiAgentService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Agent([Leftover] string query)
        {
            if (!await _service.IsAllowedAsync(ctx.User, ctx.Guild))
            {
                await Response().Error(strs.agent_not_allowed).SendAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                await Response().Error(strs.agent_no_query).SendAsync();
                return;
            }

            await _service.TryRunAgentAsync(ctx.Guild, (ITextChannel)ctx.Channel, ctx.Message, query);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task AgentCancel()
        {
            if (!await _service.IsAllowedAsync(ctx.User, ctx.Guild))
            {
                await Response().Error(strs.agent_not_allowed).SendAsync();
                return;
            }

            if (_service.CancelSession(ctx.User.Id))
                await Response().Confirm(strs.agent_cancelled).SendAsync();
            else
                await Response().Error(strs.agent_no_session).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public Task AgentSkillAdd(string name, [Leftover] string instruction)
            => AddSkillInternalAsync(name, instruction, null);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public Task AgentSkillRemove(string name)
            => RemoveSkillInternalAsync(name, null);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public Task AgentSkillToggle(string name)
            => ToggleSkillInternalAsync(name, null);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(ChannelPerm.EmbedLinks)]
        public Task AgentSkillList()
            => SkillListInternalAsync(null);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public Task AgentChannelSkillAdd(string name, [Leftover] string instruction)
            => AddSkillInternalAsync(name, instruction, ctx.Channel.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public Task AgentChannelSkillRemove(string name)
            => RemoveSkillInternalAsync(name, ctx.Channel.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public Task AgentChannelSkillToggle(string name)
            => ToggleSkillInternalAsync(name, ctx.Channel.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(ChannelPerm.EmbedLinks)]
        public Task AgentChannelSkillList()
            => SkillListInternalAsync(ctx.Channel.Id);

        private async Task AddSkillInternalAsync(string name, string instruction, ulong? channelId)
        {
            if (name.Length > AiAgent.AiAgentService.MAX_SKILL_NAME_LENGTH)
            {
                await Response().Error(strs.skill_name_too_long).SendAsync();
                return;
            }

            if (instruction.Length > AiAgent.AiAgentService.MAX_SKILL_INSTRUCTION_LENGTH)
            {
                await Response().Error(strs.skill_instruction_too_long).SendAsync();
                return;
            }

            if (await _service.AddSkillAsync(ctx.Guild.Id, name, instruction, channelId))
                await Response().Confirm(strs.skill_added(name)).SendAsync();
            else
                await Response().Error(strs.skill_add_failed(name)).SendAsync();
        }

        private async Task RemoveSkillInternalAsync(string name, ulong? channelId)
        {
            if (await _service.RemoveSkillAsync(ctx.Guild.Id, name, channelId))
                await Response().Confirm(strs.skill_removed(name)).SendAsync();
            else
                await Response().Error(strs.skill_not_found(name)).SendAsync();
        }

        private async Task ToggleSkillInternalAsync(string name, ulong? channelId)
        {
            var result = await _service.ToggleSkillAsync(ctx.Guild.Id, name, channelId);
            if (result is null)
            {
                await Response().Error(strs.skill_not_found(name)).SendAsync();
                return;
            }

            if (result.Value)
                await Response().Confirm(strs.skill_enabled(name)).SendAsync();
            else
                await Response().Confirm(strs.skill_disabled(name)).SendAsync();
        }

        private async Task SkillListInternalAsync(ulong? channelId)
        {
            var skills = _service.GetSkills(ctx.Guild.Id, channelId);
            if (skills.Count == 0)
            {
                await Response().Pending(strs.skill_list_empty).SendAsync();
                return;
            }

            var guildId = ctx.Guild.Id;
            var userId = ctx.User.Id;
            var handlers = new List<NadekoButtonInteractionHandler>();

            (EmbedBuilder Embed, ComponentBuilder Components) BuildPanel(
                IReadOnlyList<AiAgentGuildSkill> currentSkills)
            {
                var title = channelId is null
                    ? GetText(strs.skill_list_title)
                    : GetText(strs.skill_list_title_channel);

                var sb = new StringBuilder();
                for (var i = 0; i < currentSkills.Count; i++)
                {
                    var skill = currentSkills[i];
                    var icon = skill.IsEnabled ? "\u2705" : "\u274C";
                    sb.Append(icon).Append(" `").Append(i + 1).Append(".` **").Append(skill.Name).AppendLine("**");
                }

                var eb = CreateEmbed()
                    .WithTitle(title)
                    .WithDescription(sb.ToString())
                    .WithFooter(GetText(strs.skill_list_footer))
                    .WithOkColor();

                var cb = new ComponentBuilder();
                handlers.Clear();

                for (var i = 0; i < currentSkills.Count; i++)
                {
                    var skill = currentSkills[i];

                    var btn = new ButtonBuilder()
                        .WithCustomId($"agentskill:toggle:{skill.Id}:{Guid.NewGuid():N}")
                        .WithLabel((i + 1).ToString())
                        .WithStyle(skill.IsEnabled ? ButtonStyle.Success : ButtonStyle.Danger);

                    var handler = new NadekoButtonInteractionHandler(
                        client,
                        userId,
                        btn,
                        async smc =>
                        {
                            await smc.DeferAsync();
                            await _service.ToggleSkillAsync(guildId, skill.Name, channelId);

                            foreach (var h in handlers)
                                h.SetCompleted();

                            var (newEmbed, newComponents) = BuildPanel(_service.GetSkills(guildId, channelId));

                            await smc.Message.ModifyAsync(m =>
                            {
                                m.Embed = newEmbed.Build();
                                m.Components = newComponents.Build();
                            });

                            await RunHandlersInternalAsync(handlers, smc.Message);
                        },
                        onlyAuthor: true,
                        singleUse: false,
                        clearAfter: false);

                    cb.WithButton(btn, i / 5);
                    handlers.Add(handler);
                }

                return (eb, cb);
            }

            var (embed, components) = BuildPanel(skills);

            var msg = await ctx.Channel.SendMessageAsync(
                embed: embed.Build(),
                components: components.Build());

            await RunHandlersInternalAsync(handlers, msg);
            await msg.ModifyAsync(m => m.Components = new ComponentBuilder().Build());
        }

        private static Task RunHandlersInternalAsync(
            List<NadekoButtonInteractionHandler> handlers,
            IUserMessage msg)
        {
            var tasks = new Task[handlers.Count];
            for (var i = 0; i < handlers.Count; i++)
                tasks[i] = handlers[i].RunAsync(msg);

            return Task.WhenAll(tasks);
        }

        [Cmd]
        [OwnerOnly]
        public Task AgentWhitelist(IUser user)
            => ToggleWhitelistInternalAsync(
                AiAgentWhitelistType.User,
                user.Id,
                user.ToString() ?? user.Username);

        [Cmd]
        [OwnerOnly]
        public Task AgentWhitelist(ulong userId)
            => ToggleWhitelistInternalAsync(AiAgentWhitelistType.User, userId, userId.ToString());

        [Cmd]
        [OwnerOnly]
        public Task AgentWhitelistRole(IRole role)
            => ToggleWhitelistInternalAsync(AiAgentWhitelistType.Role, role.Id, role.Name);

        [Cmd]
        [OwnerOnly]
        [RequireContext(ContextType.Guild)]
        public Task AgentWhitelistServer()
            => ToggleWhitelistInternalAsync(AiAgentWhitelistType.Server, ctx.Guild.Id, ctx.Guild.Name);

        [Cmd]
        [OwnerOnly]
        public Task AgentWhitelistServer(ulong guildId)
            => ToggleWhitelistInternalAsync(
                AiAgentWhitelistType.Server,
                guildId,
                client.GetGuild(guildId)?.Name ?? guildId.ToString());

        private async Task ToggleWhitelistInternalAsync(AiAgentWhitelistType type, ulong id, string name)
        {
            var added = await whitelist.ToggleAsync(type, id);

            if (added)
                await Response().Confirm(strs.agent_whitelist_added(Format.Bold(name), id)).SendAsync();
            else
                await Response().Confirm(strs.agent_whitelist_removed(Format.Bold(name), id)).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        [BotPerm(ChannelPerm.EmbedLinks)]
        public async Task AgentWhitelistList()
        {
            var users = whitelist.GetSet(AiAgentWhitelistType.User);
            var roles = whitelist.GetSet(AiAgentWhitelistType.Role);
            var servers = whitelist.GetSet(AiAgentWhitelistType.Server);

            if (users.Count == 0 && roles.Count == 0 && servers.Count == 0)
            {
                await Response().Pending(strs.agent_whitelist_empty).SendAsync();
                return;
            }

            var eb = CreateEmbed()
                .WithTitle(GetText(strs.agent_whitelist_title))
                .WithOkColor();

            AppendSectionInternal(eb, GetText(strs.agent_whitelist_users), users);
            AppendSectionInternal(eb, GetText(strs.agent_whitelist_roles), roles);
            AppendSectionInternal(eb, GetText(strs.agent_whitelist_servers), servers);

            await Response().Embed(eb).SendAsync();
        }

        private static void AppendSectionInternal(
            EmbedBuilder eb,
            string title,
            IReadOnlyCollection<ulong> ids)
        {
            if (ids.Count == 0)
                return;

            const int maxShown = 30;

            var sb = new StringBuilder();
            var shown = 0;
            foreach (var id in ids)
            {
                if (shown++ == maxShown)
                    break;

                sb.Append('`').Append(id).AppendLine("`");
            }

            if (ids.Count > maxShown)
                sb.Append("+ ").Append(ids.Count - maxShown).Append(" more");

            eb.AddField($"{title} ({ids.Count})", sb.ToString());
        }
    }
}
