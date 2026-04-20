using NadekoBot.Db.Models;
using NadekoBot.Modules.Utility.AiAgent;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class AiAgentCommands(DiscordSocketClient client) : NadekoModule<AiAgent.AiAgentService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Agent([Leftover] string query)
        {
            if (!await _service.IsAllowedAsync(ctx.User))
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
            if (!await _service.IsAllowedAsync(ctx.User))
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
        public async Task AgentSkillAdd(string name, [Leftover] string instruction)
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

            if (await _service.AddSkillAsync(ctx.Guild.Id, name, instruction))
                await Response().Confirm(strs.skill_added(name)).SendAsync();
            else
                await Response().Error(strs.skill_add_failed(name)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AgentSkillRemove(string name)
        {
            if (await _service.RemoveSkillAsync(ctx.Guild.Id, name))
                await Response().Confirm(strs.skill_removed(name)).SendAsync();
            else
                await Response().Error(strs.skill_not_found(name)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AgentSkillToggle(string name)
        {
            var result = await _service.ToggleSkillAsync(ctx.Guild.Id, name);
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

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AgentSkillList()
        {
            var skills = _service.GetSkills(ctx.Guild.Id);
            if (skills.Count == 0)
            {
                await Response().Pending(strs.skill_list_empty).SendAsync();
                return;
            }

            var guildId = ctx.Guild.Id;
            var userId = ctx.User.Id;
            var handlers = new List<NadekoButtonInteractionHandler>();

            (EmbedBuilder Embed, ComponentBuilder Components) BuildSkillPanel(
                IReadOnlyList<AiAgentGuildSkill> currentSkills)
            {
                var eb = CreateEmbed()
                    .WithTitle(GetText(strs.skill_list_title))
                    .WithOkColor();

                var sb = new System.Text.StringBuilder();
                for (var i = 0; i < currentSkills.Count; i++)
                {
                    var skill = currentSkills[i];
                    var icon = skill.IsEnabled ? "\u2705" : "\u274C";
                    sb.AppendLine($"{icon} `{i + 1}.` **{skill.Name}**");
                }

                eb.WithDescription(sb.ToString())
                  .WithFooter("Changes apply on next agent session");

                var cb = new ComponentBuilder();
                handlers.Clear();

                for (var i = 0; i < currentSkills.Count; i++)
                {
                    var skill = currentSkills[i];
                    var row = i / 5;
                    var customId = $"agentskill:toggle:{skill.Name}:{Guid.NewGuid():N}";

                    var btn = new ButtonBuilder()
                        .WithCustomId(customId)
                        .WithLabel($"{i + 1}")
                        .WithStyle(skill.IsEnabled ? ButtonStyle.Success : ButtonStyle.Danger);

                    var handler = new NadekoButtonInteractionHandler(
                        client,
                        userId,
                        btn,
                        async smc =>
                        {
                            await smc.DeferAsync();
                            await _service.ToggleSkillAsync(guildId, skill.Name);

                            foreach (var h in handlers)
                                h.SetCompleted();

                            var refreshedSkills = _service.GetSkills(guildId);
                            var (newEmbed, newComponents) = BuildSkillPanel(refreshedSkills);

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
                        },
                        onlyAuthor: true,
                        singleUse: false,
                        clearAfter: false);

                    cb.WithButton(btn, row);
                    handlers.Add(handler);
                }

                return (eb, cb);
            }

            var (embed, components) = BuildSkillPanel(skills);

            var msg = await ctx.Channel.SendMessageAsync(
                embed: embed.Build(),
                components: components.Build());

            var tasks = new Task[handlers.Count];
            for (var i = 0; i < handlers.Count; i++)
                tasks[i] = handlers[i].RunAsync(msg);
        
            await Task.WhenAll(tasks);
            await msg.ModifyAsync(m => m.Components = new ComponentBuilder().Build()); 
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AgentChannelSkillAdd(string name, [Leftover] string instruction)
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

            var channelId = ctx.Channel.Id;
            if (await _service.AddSkillAsync(ctx.Guild.Id, name, instruction, channelId))
                await Response().Confirm(strs.skill_added(name)).SendAsync();
            else
                await Response().Error(strs.skill_add_failed(name)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AgentChannelSkillRemove(string name)
        {
            var channelId = ctx.Channel.Id;
            if (await _service.RemoveSkillAsync(ctx.Guild.Id, name, channelId))
                await Response().Confirm(strs.skill_removed(name)).SendAsync();
            else
                await Response().Error(strs.skill_not_found(name)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AgentChannelSkillToggle(string name)
        {
            var channelId = ctx.Channel.Id;
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

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AgentChannelSkillList()
        {
            var channelId = ctx.Channel.Id;
            var skills = _service.GetSkills(ctx.Guild.Id, channelId);
            if (skills.Count == 0)
            {
                await Response().Pending(strs.skill_list_empty).SendAsync();
                return;
            }

            var guildId = ctx.Guild.Id;
            var userId = ctx.User.Id;
            var handlers = new List<NadekoButtonInteractionHandler>();

            (EmbedBuilder Embed, ComponentBuilder Components) BuildChannelSkillPanel(
                IReadOnlyList<AiAgentGuildSkill> currentSkills)
            {
                var eb = CreateEmbed()
                    .WithTitle(GetText(strs.skill_list_title) + " (channel)")
                    .WithOkColor();

                var sb = new System.Text.StringBuilder();
                for (var i = 0; i < currentSkills.Count; i++)
                {
                    var skill = currentSkills[i];
                    var icon = skill.IsEnabled ? "\u2705" : "\u274C";
                    sb.AppendLine($"{icon} `{i + 1}.` **{skill.Name}**");
                }

                eb.WithDescription(sb.ToString())
                  .WithFooter("Changes apply on next agent session");

                var cb = new ComponentBuilder();
                handlers.Clear();

                for (var i = 0; i < currentSkills.Count; i++)
                {
                    var skill = currentSkills[i];
                    var row = i / 5;
                    var customId = $"agentchskill:toggle:{skill.Name}:{Guid.NewGuid():N}";

                    var btn = new ButtonBuilder()
                        .WithCustomId(customId)
                        .WithLabel($"{i + 1}")
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

                            var refreshedSkills = _service.GetSkills(guildId, channelId);
                            var (newEmbed, newComponents) = BuildChannelSkillPanel(refreshedSkills);

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
                        },
                        onlyAuthor: true,
                        singleUse: false,
                        clearAfter: false);

                    cb.WithButton(btn, row);
                    handlers.Add(handler);
                }

                return (eb, cb);
            }

            var (embed, components) = BuildChannelSkillPanel(skills);

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
