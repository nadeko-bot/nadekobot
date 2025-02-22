namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class GreetCommands : NadekoModule<GreetService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task Boost()
            => Toggle(GreetType.Boost);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task BoostDel(int timer = 30)
            => SetDel(GreetType.Boost, timer);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task BoostMsg([Leftover] string? text = null)
            => SetMsg(GreetType.Boost, text);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task Greet()
            => Toggle(GreetType.Greet);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task GreetDel(int timer = 30)
            => SetDel(GreetType.Greet, timer);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task GreetMsg([Leftover] string? text = null)
            => SetMsg(GreetType.Greet, text);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task GreetDm()
            => Toggle(GreetType.GreetDm);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task GreetDmMsg([Leftover] string? text = null)
            => SetMsg(GreetType.GreetDm, text);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task Bye()
            => Toggle(GreetType.Bye);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task ByeDel(int timer = 30)
            => SetDel(GreetType.Bye, timer);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task ByeMsg([Leftover] string? text = null)
            => SetMsg(GreetType.Bye, text);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task GreetTest([Leftover] IGuildUser? user = null)
            => Test(GreetType.Greet, user);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public Task GreetDmTest([Leftover] IGuildUser? user = null)
            => Test(GreetType.GreetDm, user);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        [Ratelimit(5)]
        public Task ByeTest([Leftover] IGuildUser? user = null)
            => Test(GreetType.Bye, user);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        [Ratelimit(5)]
        public Task BoostTest([Leftover] IGuildUser? user = null)
            => Test(GreetType.Boost, user);


        public async Task Toggle(GreetType type)
        {
            var enabled = await _service.SetGreet(ctx.Guild.Id, ctx.Channel.Id, type);

            if (enabled)
                await Response()
                      .Confirm(
                          type switch
                          {
                              GreetType.Boost => strs.boost_on,
                              GreetType.Greet => strs.greet_on,
                              GreetType.Bye => strs.bye_on,
                              GreetType.GreetDm => strs.greetdm_on,
                              _ => strs.error
                          }
                      )
                      .SendAsync();
            else
                await Response()
                      .Pending(
                          type switch
                          {
                              GreetType.Boost => strs.boost_off,
                              GreetType.Greet => strs.greet_off,
                              GreetType.Bye => strs.bye_off,
                              GreetType.GreetDm => strs.greetdm_off,
                              _ => strs.error
                          }
                      )
                      .SendAsync();
        }


        public async Task SetDel(GreetType type, int timer)
        {
            if (timer is < 0 or > 600)
                return;

            await _service.SetDeleteTimer(ctx.Guild.Id, type, timer);

            if (timer > 0)
                await Response()
                      .Confirm(
                          type switch
                          {
                              GreetType.Boost => strs.boostdel_on(timer),
                              GreetType.Greet => strs.greetdel_on(timer),
                              GreetType.Bye => strs.byedel_on(timer),
                              _ => strs.error
                          }
                      )
                      .SendAsync();
            else
                await Response()
                      .Pending(
                          type switch
                          {
                              GreetType.Boost => strs.boostdel_off,
                              GreetType.Greet => strs.greetdel_off,
                              GreetType.Bye => strs.byedel_off,
                              _ => strs.error
                          })
                      .SendAsync();
        }


        public async Task SetMsg(GreetType type, string? text = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                var conf = await _service.GetGreetSettingsAsync(ctx.Guild.Id, type);
                var msg = conf?.MessageText ?? GreetService.GetDefaultGreet(type);
                await Response()
                      .Confirm(
                          type switch
                          {
                              GreetType.Boost => strs.boostmsg_cur(msg),
                              GreetType.Greet => strs.greetmsg_cur(msg),
                              GreetType.Bye => strs.byemsg_cur(msg),
                              GreetType.GreetDm => strs.greetdmmsg_cur(msg),
                              _ => strs.error
                          })
                      .SendAsync();
                return;
            }

            var isEnabled = await _service.SetMessage(ctx.Guild.Id, type, text);

            await Response()
                  .Confirm(type switch
                  {
                      GreetType.Boost => strs.boostmsg_new,
                      GreetType.Greet => strs.greetmsg_new,
                      GreetType.Bye => strs.byemsg_new,
                      GreetType.GreetDm => strs.greetdmmsg_new,
                      _ => strs.error
                  })
                  .SendAsync();


            if (!isEnabled)
            {
                await SendGreetEnableHint(type);
            }
        }

        private static string GetCmdName(GreetType type)
        {
            var cmdName = type switch
            {
                GreetType.Greet => "greet",
                GreetType.Bye => "bye",
                GreetType.Boost => "boost",
                GreetType.GreetDm => "greetdm",
                _ => "unknown_command"
            };
            return cmdName;
        }

        public async Task Test(GreetType type, IGuildUser? user = null)
        {
            user ??= (IGuildUser)ctx.User;

            await _service.Test(ctx.Guild.Id, type, (ITextChannel)ctx.Channel, user);
            var conf = await _service.GetGreetSettingsAsync(ctx.Guild.Id, type);

            if (conf?.IsEnabled is not true)
                await SendGreetEnableHint(type);
        }

        private async Task SendGreetEnableHint(GreetType type)
        {
            var cmd = $"`{prefix}{GetCmdName(type)}`";

            var str = type switch
            {
                GreetType.Greet => strs.greetmsg_enable(cmd),
                GreetType.Bye => strs.byemsg_enable(cmd),
                GreetType.Boost => strs.boostmsg_enable(cmd),
                GreetType.GreetDm => strs.greetdmmsg_enable(cmd),
                _ => strs.error
            };

            await Response().Pending(str).SendAsync();
        }
    }
}