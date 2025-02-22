#nullable disable
using CommandLine;
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class UserPunishCommands : NadekoModule<UserPunishService>
    {
        public enum AddRole
        {
            AddRole
        }

        private readonly MuteService _mute;

        public UserPunishCommands(MuteService mute)
        {
            _mute = mute;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        public Task Warn(IGuildUser user, [Leftover] string reason = null)
            => Warn(1, user, reason);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        public async Task Warn(int weight, IGuildUser user, [Leftover] string reason = null)
        {
            if (weight <= 0)
                return;

            if (!await CheckRoleHierarchy(user))
                return;

            var dmFailed = false;
            try
            {
                await _sender.Response(user)
                             .Embed(CreateEmbed()
                                           .WithErrorColor()
                                           .WithDescription(GetText(strs.warned_on(ctx.Guild.ToString())))
                                           .AddField(GetText(strs.moderator), ctx.User.ToString())
                                           .AddField(GetText(strs.reason), reason ?? "-"))
                             .SendAsync();
            }
            catch
            {
                dmFailed = true;
            }

            WarningPunishment punishment;
            try
            {
                punishment = await _service.Warn(ctx.Guild, user.Id, ctx.User, weight, reason);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Exception occured while warning a user");
                var errorEmbed = CreateEmbed()
                                        .WithErrorColor()
                                        .WithDescription(GetText(strs.cant_apply_punishment));

                if (dmFailed)
                    errorEmbed.WithFooter("⚠️ " + GetText(strs.unable_to_dm_user));

                await Response().Embed(errorEmbed).SendAsync();
                return;
            }

            var embed = CreateEmbed().WithOkColor();
            if (punishment is null)
                embed.WithDescription(GetText(strs.user_warned(Format.Bold(user.ToString()))));
            else
            {
                embed.WithDescription(GetText(strs.user_warned_and_punished(Format.Bold(user.ToString()),
                    Format.Bold(punishment.Punishment.ToString()))));
            }

            if (dmFailed)
                embed.WithFooter("⚠️ " + GetText(strs.unable_to_dm_user));

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [NadekoOptions<WarnExpireOptions>]
        [Priority(1)]
        public async Task WarnExpire()
        {
            var (expireDays, _) = await _service.GetWarnExpire(ctx.Guild.Id);

            if (expireDays == 0)
                await Response().Confirm(strs.warns_dont_expire).SendAsync();
            else
                await Response().Error(strs.warns_expire_in(expireDays)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [NadekoOptions<WarnExpireOptions>]
        [Priority(2)]
        public async Task WarnExpire(int days, params string[] args)
        {
            if (days is < 0 or > 366)
                return;

            var opts = OptionsParser.ParseFrom<WarnExpireOptions>(args);

            await ctx.Channel.TriggerTypingAsync();

            await _service.WarnExpireAsync(ctx.Guild.Id, days, opts.Delete);
            if (days == 0)
            {
                await Response().Confirm(strs.warn_expire_reset).SendAsync();
                return;
            }

            if (opts.Delete)
                await Response().Confirm(strs.warn_expire_set_delete(Format.Bold(days.ToString()))).SendAsync();
            else
                await Response().Confirm(strs.warn_expire_set_clear(Format.Bold(days.ToString()))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [Priority(2)]
        public Task Warnlog(int page, [Leftover] IGuildUser user = null)
        {
            user ??= (IGuildUser)ctx.User;

            return Warnlog(page, user.Id);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(3)]
        public Task Warnlog(IGuildUser user = null)
        {
            user ??= (IGuildUser)ctx.User;

            return ctx.User.Id == user.Id || ((IGuildUser)ctx.User).GuildPermissions.BanMembers
                ? Warnlog(user.Id)
                : Task.CompletedTask;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [Priority(0)]
        public Task Warnlog(int page, ulong userId)
            => InternalWarnlog(userId, page - 1);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [Priority(1)]
        public Task Warnlog(ulong userId)
            => InternalWarnlog(userId, 0);

        private async Task InternalWarnlog(ulong userId, int inputPage)
        {
            if (inputPage < 0)
                return;

            var allWarnings = _service.UserWarnings(ctx.Guild.Id, userId);

            await Response()
                  .Paginated()
                  .Items(allWarnings)
                  .PageSize(9)
                  .CurrentPage(inputPage)
                  .Page((warnings, page) =>
                  {
                      var user = (ctx.Guild as SocketGuild)?.GetUser(userId)?.ToString() ?? userId.ToString();
                      var embed = CreateEmbed().WithOkColor().WithTitle(GetText(strs.warnlog_for(user)));

                      if (!warnings.Any())
                          embed.WithDescription(GetText(strs.warnings_none));
                      else
                      {
                          var descText = GetText(strs.warn_count(
                              Format.Bold(warnings.Where(x => !x.Forgiven).Sum(x => x.Weight).ToString()),
                              Format.Bold(warnings.Sum(x => x.Weight).ToString())));

                          embed.WithDescription(descText);

                          var i = page * 9;
                          foreach (var w in warnings)
                          {
                              i++;
                              var name = GetText(strs.warned_on_by(w.DateAdded?.ToString("dd.MM.yyy"),
                                  w.DateAdded?.ToString("HH:mm"),
                                  w.Moderator));

                              if (w.Forgiven)
                                  name = $"{Format.Strikethrough(name)} {GetText(strs.warn_cleared_by(w.ForgivenBy))}";


                              embed.AddField($"#`{i}` " + name,
                                  Format.Code(GetText(strs.warn_weight(w.Weight))) + '\n' + w.Reason.TrimTo(1000));
                          }
                      }

                      return embed;
                  })
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        public async Task WarnlogAll(int page = 1)
        {
            if (--page < 0)
                return;
            var allWarnings = _service.WarnlogAll(ctx.Guild.Id);

            await Response()
                  .Paginated()
                  .Items(allWarnings)
                  .PageSize(15)
                  .CurrentPage(page)
                  .Page((warnings, _) =>
                  {
                      var ws = warnings
                          .Select(x =>
                          {
                              var all = x.Count();
                              var forgiven = x.Count(y => y.Forgiven);
                              var total = all - forgiven;
                              var usr = ((SocketGuild)ctx.Guild).GetUser(x.Key);
                              return (usr?.ToString() ?? x.Key.ToString())
                                     + $" | {total} ({all} - {forgiven})";
                          });

                      return CreateEmbed()
                                    .WithOkColor()
                                    .WithTitle(GetText(strs.warnings_list))
                                    .WithDescription(string.Join("\n", ws));
                  })
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public Task WarnDelete(IGuildUser user, int index)
            => WarnDelete(user.Id, index);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task WarnDelete(ulong userId, int index)
        {
            if (--index < 0)
                return;

            var warn = await _service.WarnDelete(ctx.Guild.Id, userId, index);

            if (warn is null)
            {
                await Response().Error(strs.warning_not_found).SendAsync();
                return;
            }

            await Response().Confirm(strs.warning_deleted(Format.Bold(index.ToString()))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        public Task Warnclear(IGuildUser user, int index = 0)
            => Warnclear(user.Id, index);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        public async Task Warnclear(ulong userId, int index = 0)
        {
            if (index < 0)
                return;

            var success = await _service.WarnClearAsync(ctx.Guild.Id, userId, index, ctx.User.ToString());
            var userStr = Format.Bold((ctx.Guild as SocketGuild)?.GetUser(userId)?.ToString() ?? userId.ToString());
            if (index == 0)
                await Response().Error(strs.warnings_cleared(userStr)).SendAsync();
            else
            {
                if (success)
                    await Response().Confirm(strs.warning_cleared(Format.Bold(index.ToString()), userStr)).SendAsync();
                else
                    await Response().Error(strs.warning_clear_fail).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [Priority(1)]
        public async Task WarnPunish(
            int number,
            AddRole _,
            IRole role,
            ParsedTimespan timespan = null)
        {
            var punish = PunishmentAction.AddRole;

            if (ctx.Guild.OwnerId != ctx.User.Id
                && role.Position >= ((IGuildUser)ctx.User).GetRoles().Max(x => x.Position))
            {
                await Response().Error(strs.role_too_high).SendAsync();
                return;
            }

            var success = await _service.WarnPunish(ctx.Guild.Id, number, punish, timespan, role);

            if (!success)
                return;

            if (timespan is null)
            {
                await Response()
                      .Confirm(strs.warn_punish_set(Format.Bold(punish.ToString()),
                          Format.Bold(number.ToString())))
                      .SendAsync();
            }
            else
            {
                await Response()
                      .Confirm(strs.warn_punish_set_timed(Format.Bold(punish.ToString()),
                          Format.Bold(number.ToString()),
                          Format.Bold(timespan.Input)))
                      .SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        public async Task WarnPunish(int number, PunishmentAction punish, ParsedTimespan timespan = null)
        {
            // this should never happen. Addrole has its own method with higher priority
            // also disallow warn punishment for getting warned
            if (punish is PunishmentAction.AddRole or PunishmentAction.Warn)
                return;

            // you must specify the time for timeout
            if (punish is PunishmentAction.TimeOut && timespan is null)
                return;

            var success = await _service.WarnPunish(ctx.Guild.Id, number, punish, timespan);

            if (!success)
                return;

            if (timespan is null)
            {
                await Response()
                      .Confirm(strs.warn_punish_set(Format.Bold(punish.ToString()),
                          Format.Bold(number.ToString())))
                      .SendAsync();
            }
            else
            {
                await Response()
                      .Confirm(strs.warn_punish_set_timed(Format.Bold(punish.ToString()),
                          Format.Bold(number.ToString()),
                          Format.Bold(timespan.Input)))
                      .SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        public async Task WarnPunish(int number)
        {
            if (!await _service.WarnPunishRemove(ctx.Guild.Id, number))
                return;

            await Response().Confirm(strs.warn_punish_rem(Format.Bold(number.ToString()))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task WarnPunishList()
        {
            var ps = await _service.WarnPunishList(ctx.Guild.Id);

            string list;
            if (ps.Any())
            {
                list = string.Join("\n",
                    ps.Select(x
                        => $"{x.Count} -> {x.Punishment} {(x.Punishment == PunishmentAction.AddRole ? $"<@&{x.RoleId}>" : "")} {(x.Time <= 0 ? "" : x.Time + "m")} "));
            }
            else
                list = GetText(strs.warnpl_none);

            await Response().Confirm(GetText(strs.warn_punish_list), list).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        [Priority(1)]
        public Task Ban(ParsedTimespan timespan, IUser user, [Leftover] string msg = null)
            => Ban(timespan, user.Id, msg);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        [Priority(0)]
        public async Task Ban(ParsedTimespan timespan, ulong userId, [Leftover] string msg = null)
        {
            if (timespan.Time > TimeSpan.FromDays(49))
                return;

            var guildUser = await ((DiscordSocketClient)Context.Client).Rest.GetGuildUserAsync(ctx.Guild.Id, userId);


            if (guildUser is not null && !await CheckRoleHierarchy(guildUser))
                return;

            var dmFailed = false;

            if (guildUser is not null)
            {
                try
                {
                    var defaultMessage = GetText(strs.bandm(Format.Bold(ctx.Guild.Name), msg));
                    var smartText =
                        await _service.GetBanUserDmEmbed(Context, guildUser, defaultMessage, msg, timespan.Time);
                    if (smartText is not null)
                        await Response().User(guildUser).Text(smartText).SendAsync();
                }
                catch
                {
                    dmFailed = true;
                }
            }

            var user = await ctx.Client.GetUserAsync(userId);
            var banPrune = await _service.GetBanPruneAsync(ctx.Guild.Id) ?? 7;
            await _mute.TimedBan(ctx.Guild, userId, timespan.Time, (ctx.User + " | " + msg).TrimTo(512), banPrune);
            var toSend = CreateEmbed()
                                .WithOkColor()
                                .WithTitle("⛔️ " + GetText(strs.banned_user))
                                .AddField(GetText(strs.username), user?.ToString() ?? userId.ToString(), true)
                                .AddField("ID", userId.ToString(), true)
                                .AddField(GetText(strs.duration),
                                    timespan.Time.ToPrettyStringHm(),
                                    true);

            if (dmFailed)
                toSend.WithFooter("⚠️ " + GetText(strs.unable_to_dm_user));

            await Response().Embed(toSend).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        [Priority(0)]
        public async Task Ban(ulong userId, [Leftover] string msg = null)
        {
            var user = await ((DiscordSocketClient)Context.Client).Rest.GetGuildUserAsync(ctx.Guild.Id, userId);
            if (user is null)
            {
                var banPrune = await _service.GetBanPruneAsync(ctx.Guild.Id) ?? 7;
                await ctx.Guild.AddBanAsync(userId, banPrune, (ctx.User + " | " + msg).TrimTo(512));

                await Response()
                      .Embed(CreateEmbed()
                                    .WithOkColor()
                                    .WithTitle("⛔️ " + GetText(strs.banned_user))
                                    .AddField("ID", userId.ToString(), true))
                      .SendAsync();
            }
            else
                await Ban(user, msg);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        [Priority(2)]
        public async Task Ban(IGuildUser user, [Leftover] string msg = null)
        {
            if (!await CheckRoleHierarchy(user))
                return;

            var dmFailed = false;

            try
            {
                var defaultMessage = GetText(strs.bandm(Format.Bold(ctx.Guild.Name), msg));
                var embed = await _service.GetBanUserDmEmbed(Context, user, defaultMessage, msg, null);
                if (embed is not null)
                    await Response().User(user).Text(embed).SendAsync();
            }
            catch
            {
                dmFailed = true;
            }

            var banPrune = await _service.GetBanPruneAsync(ctx.Guild.Id) ?? 7;
            await ctx.Guild.AddBanAsync(user, banPrune, (ctx.User + " | " + msg).TrimTo(512));

            var toSend = CreateEmbed()
                                .WithOkColor()
                                .WithTitle("⛔️ " + GetText(strs.banned_user))
                                .AddField(GetText(strs.username), user.ToString(), true)
                                .AddField("ID", user.Id.ToString(), true);

            if (dmFailed)
                toSend.WithFooter("⚠️ " + GetText(strs.unable_to_dm_user));

            await Response().Embed(toSend).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        public async Task BanPrune(int days)
        {
            if (days < 0 || days > 7)
            {
                await Response().Error(strs.invalid_input).SendAsync();
                return;
            }

            await _service.SetBanPruneAsync(ctx.Guild.Id, days);

            if (days == 0)
                await Response().Confirm(strs.ban_prune_disabled).SendAsync();
            else
                await Response().Confirm(strs.ban_prune(days)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        public async Task BanMessage([Leftover] string message = null)
        {
            if (message is null)
            {
                var template = _service.GetBanTemplate(ctx.Guild.Id);
                if (template is null)
                {
                    await Response().Confirm(strs.banmsg_default).SendAsync();
                    return;
                }

                await Response().Confirm(template).SendAsync();
                return;
            }

            _service.SetBanTemplate(ctx.Guild.Id, message);
            await ctx.OkAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        public async Task BanMsgReset()
        {
            _service.SetBanTemplate(ctx.Guild.Id, null);
            await ctx.OkAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        [Priority(0)]
        public Task BanMessageTest([Leftover] string reason = null)
            => InternalBanMessageTest(reason, null);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        [Priority(1)]
        public Task BanMessageTest(ParsedTimespan duration, [Leftover] string reason = null)
            => InternalBanMessageTest(reason, duration.Time);

        private async Task InternalBanMessageTest(string reason, TimeSpan? duration)
        {
            var defaultMessage = GetText(strs.bandm(Format.Bold(ctx.Guild.Name), reason));
            var smartText = await _service.GetBanUserDmEmbed(Context,
                (IGuildUser)ctx.User,
                defaultMessage,
                reason,
                duration);

            if (smartText is null)
                await Response().Confirm(strs.banmsg_disabled).SendAsync();
            else
            {
                try
                {
                    await Response().User(ctx.User).Text(smartText).SendAsync();
                }
                catch (Exception)
                {
                    await Response().Error(strs.unable_to_dm_user).SendAsync();
                    return;
                }

                await ctx.OkAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        public async Task Unban([Leftover] string user)
        {
            var bans = await ctx.Guild.GetBansAsync().FlattenAsync();

            var bun = bans.FirstOrDefault(x => x.User.ToString()!.ToLowerInvariant() == user.ToLowerInvariant());

            if (bun is null)
            {
                await Response().Error(strs.user_not_found).SendAsync();
                return;
            }

            await UnbanInternal(bun.User);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        public async Task Unban(ulong userId)
        {
            var bun = await ctx.Guild.GetBanAsync(userId);

            if (bun is null)
            {
                await Response().Error(strs.user_not_found).SendAsync();
                return;
            }

            await UnbanInternal(bun.User);
        }

        private async Task UnbanInternal(IUser user)
        {
            await ctx.Guild.RemoveBanAsync(user);

            await Response().Confirm(strs.unbanned_user(Format.Bold(user.ToString()))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.KickMembers | GuildPerm.ManageMessages)]
        [BotPerm(GuildPerm.BanMembers)]
        public Task Softban(IGuildUser user, [Leftover] string msg = null)
            => SoftbanInternal(user, msg);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.KickMembers | GuildPerm.ManageMessages)]
        [BotPerm(GuildPerm.BanMembers)]
        public async Task Softban(ulong userId, [Leftover] string msg = null)
        {
            var user = await ((DiscordSocketClient)Context.Client).Rest.GetGuildUserAsync(ctx.Guild.Id, userId);
            if (user is null)
                return;

            await SoftbanInternal(user, msg);
        }

        private async Task SoftbanInternal(IGuildUser user, [Leftover] string msg = null)
        {
            if (!await CheckRoleHierarchy(user))
                return;

            var dmFailed = false;

            try
            {
                await Response()
                      .Channel(await user.CreateDMChannelAsync())
                      .Error(strs.sbdm(Format.Bold(ctx.Guild.Name), msg))
                      .SendAsync();
            }
            catch
            {
                dmFailed = true;
            }

            var banPrune = await _service.GetBanPruneAsync(ctx.Guild.Id) ?? 7;
            await ctx.Guild.AddBanAsync(user, banPrune, ("Softban | " + ctx.User + " | " + msg).TrimTo(512));
            try { await ctx.Guild.RemoveBanAsync(user); }
            catch { await ctx.Guild.RemoveBanAsync(user); }

            var toSend = CreateEmbed()
                                .WithOkColor()
                                .WithTitle("☣ " + GetText(strs.sb_user))
                                .AddField(GetText(strs.username), user.ToString(), true)
                                .AddField("ID", user.Id.ToString(), true);

            if (dmFailed)
                toSend.WithFooter("⚠️ " + GetText(strs.unable_to_dm_user));

            await Response().Embed(toSend).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.KickMembers)]
        [BotPerm(GuildPerm.KickMembers)]
        [Priority(1)]
        public Task Kick(IGuildUser user, [Leftover] string msg = null)
            => KickInternal(user, msg);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.KickMembers)]
        [BotPerm(GuildPerm.KickMembers)]
        [Priority(0)]
        public async Task Kick(ulong userId, [Leftover] string msg = null)
        {
            var user = await ((DiscordSocketClient)Context.Client).Rest.GetGuildUserAsync(ctx.Guild.Id, userId);
            if (user is null)
                return;

            await KickInternal(user, msg);
        }

        private async Task KickInternal(IGuildUser user, string msg = null)
        {
            if (!await CheckRoleHierarchy(user))
                return;

            var dmFailed = false;

            try
            {
                await Response()
                      .Channel(await user.CreateDMChannelAsync())
                      .Error(GetText(strs.kickdm(Format.Bold(ctx.Guild.Name), msg)))
                      .SendAsync();
            }
            catch
            {
                dmFailed = true;
            }

            await user.KickAsync((ctx.User + " | " + msg).TrimTo(512));

            var toSend = CreateEmbed()
                                .WithOkColor()
                                .WithTitle(GetText(strs.kicked_user))
                                .AddField(GetText(strs.username), user.ToString(), true)
                                .AddField("ID", user.Id.ToString(), true);

            if (dmFailed)
                toSend.WithFooter("⚠️ " + GetText(strs.unable_to_dm_user));

            await Response().Embed(toSend).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ModerateMembers)]
        [BotPerm(GuildPerm.ModerateMembers)]
        [Priority(2)]
        public async Task Timeout(IUser globalUser, ParsedTimespan timespan, [Leftover] string msg = null)
        {
            var user = await ctx.Guild.GetUserAsync(globalUser.Id);

            if (user is null)
                return;

            if (!await CheckRoleHierarchy(user))
                return;

            var dmFailed = false;

            try
            {
                var dmMessage = GetText(strs.timeoutdm(Format.Bold(ctx.Guild.Name), msg));
                await _sender.Response(user)
                             .Embed(CreateEmbed()
                                           .WithPendingColor()
                                           .WithDescription(dmMessage))
                             .SendAsync();
            }
            catch
            {
                dmFailed = true;
            }

            await user.SetTimeOutAsync(timespan.Time);

            var toSend = CreateEmbed()
                                .WithOkColor()
                                .WithTitle("⏳ " + GetText(strs.timedout_user))
                                .AddField(GetText(strs.username), user.ToString(), true)
                                .AddField("ID", user.Id.ToString(), true);

            if (dmFailed)
                toSend.WithFooter("⚠️ " + GetText(strs.unable_to_dm_user));

            await Response().Embed(toSend).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        [Ratelimit(30)]
        public async Task MassBan(params string[] userStrings)
        {
            if (userStrings.Length == 0)
                return;

            var missing = new List<string>();
            var banning = new HashSet<IUser>();

            await ctx.Channel.TriggerTypingAsync();
            foreach (var userStr in userStrings)
            {
                if (ulong.TryParse(userStr, out var userId))
                {
                    IUser user = await ctx.Guild.GetUserAsync(userId)
                                 ?? await ((DiscordSocketClient)Context.Client).Rest.GetGuildUserAsync(ctx.Guild.Id,
                                     userId);

                    if (user is null)
                    {
                        // if IGuildUser is null, try to get IUser
                        user = await ((DiscordSocketClient)Context.Client).Rest.GetUserAsync(userId);

                        // only add to missing if *still* null
                        if (user is null)
                        {
                            missing.Add(userStr);
                            continue;
                        }
                    }

                    //Hierachy checks only if the user is in the guild
                    if (user is IGuildUser gu && !await CheckRoleHierarchy(gu))
                        return;

                    banning.Add(user);
                }
                else
                    missing.Add(userStr);
            }

            var missStr = string.Join("\n", missing);
            if (string.IsNullOrWhiteSpace(missStr))
                missStr = "-";

            var toSend = CreateEmbed()
                                .WithDescription(GetText(strs.mass_ban_in_progress(banning.Count)))
                                .AddField(GetText(strs.invalid(missing.Count)), missStr)
                                .WithPendingColor();

            var banningMessage = await Response().Embed(toSend).SendAsync();

            var banPrune = await _service.GetBanPruneAsync(ctx.Guild.Id) ?? 7;
            foreach (var toBan in banning)
            {
                try
                {
                    await ctx.Guild.AddBanAsync(toBan.Id, banPrune, $"{ctx.User} | Massban");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error banning {User} user in {GuildId} server", toBan.Id, ctx.Guild.Id);
                }
            }

            await banningMessage.ModifyAsync(x => x.Embed = CreateEmbed()
                                                                   .WithDescription(
                                                                       GetText(strs.mass_ban_completed(
                                                                           banning.Count())))
                                                                   .AddField(GetText(strs.invalid(missing.Count)),
                                                                       missStr)
                                                                   .WithOkColor()
                                                                   .Build());
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.BanMembers)]
        [BotPerm(GuildPerm.BanMembers)]
        [OwnerOnly]
        public async Task MassKill([Leftover] string people)
        {
            if (string.IsNullOrWhiteSpace(people))
                return;

            var (bans, missing) = _service.MassKill((SocketGuild)ctx.Guild, people);

            var missStr = string.Join("\n", missing);
            if (string.IsNullOrWhiteSpace(missStr))
                missStr = "-";

            //send a message but don't wait for it
            var banningMessageTask = Response()
                                     .Embed(CreateEmbed()
                                                   .WithDescription(
                                                       GetText(strs.mass_kill_in_progress(bans.Count())))
                                                   .AddField(GetText(strs.invalid(missing)), missStr)
                                                   .WithPendingColor())
                                     .SendAsync();

            var banPrune = await _service.GetBanPruneAsync(ctx.Guild.Id) ?? 7;
            //do the banning
            await Task.WhenAll(bans.Where(x => x.Id.HasValue)
                                   .Select(x => ctx.Guild.AddBanAsync(x.Id.Value,
                                       banPrune,
                                       x.Reason,
                                       new()
                                       {
                                           RetryMode = RetryMode.AlwaysRetry
                                       })));

            //wait for the message and edit it
            var banningMessage = await banningMessageTask;

            await banningMessage.ModifyAsync(x => x.Embed = CreateEmbed()
                                                                   .WithDescription(
                                                                       GetText(strs.mass_kill_completed(bans.Count())))
                                                                   .AddField(GetText(strs.invalid(missing)), missStr)
                                                                   .WithOkColor()
                                                                   .Build());
        }

        public class WarnExpireOptions : INadekoCommandOptions
        {
            [Option('d', "delete", Default = false, HelpText = "Delete warnings instead of clearing them.")]
            public bool Delete { get; set; } = false;

            public void NormalizeOptions()
            {
            }
        }
    }
}