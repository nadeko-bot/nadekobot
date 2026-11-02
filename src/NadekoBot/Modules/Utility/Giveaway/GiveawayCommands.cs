namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Name("Giveaways")]
    [Group("ga")]
    public partial class GiveawayCommands : NadekoModule<GiveawayService>
    {
        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        [BotPerm(ChannelPerm.ManageMessages | ChannelPerm.AddReactions)]
        public async Task GiveawayStart(TimeSpan duration, [Leftover] string message)
        {
            if (duration > TimeSpan.FromDays(30))
            {
                await Response().Error(strs.giveaway_duration_invalid).SendAsync();
                return;
            }

            var winnerCount = 1;
            var space = message.IndexOf(' ');
            if (space > 0
                && GiveawayService.TryParseWinnerCount(message.AsSpan(0, space), out var parsed)
                && !message.AsSpan(space).IsWhiteSpace())
            {
                winnerCount = parsed;
                message = message[(space + 1)..].TrimStart();
            }

            var eb = CreateEmbed()
                .WithPendingColor()
                .WithTitle(GetText(strs.giveaway_starting))
                .WithDescription(message);

            var startingMsg = await Response().Embed(eb).SendAsync();

            var maybeId =
                await _service.StartGiveawayAsync(ctx.Guild.Id,
                    ctx.Channel.Id,
                    startingMsg.Id,
                    duration,
                    message,
                    winnerCount);


            if (maybeId is not int id)
            {
                await startingMsg.DeleteAsync();
                await Response().Error(strs.giveaway_max_amount_reached).SendAsync();
                return;
            }

            eb
                .WithOkColor()
                .WithTitle(GetText(strs.giveaway_started))
                .AddField(GetText(strs.lasts_until), TimestampTag.FromDateTime(DateTime.UtcNow.Add(duration)), true)
                .AddField(GetText(strs.winners_count), winnerCount.ToString(), true)
                .WithFooter($"id:  {new kwum(id).ToString()}");

            await startingMsg.AddReactionAsync(new Emoji(GiveawayService.GiveawayEmoji));
            await startingMsg.ModifyAsync(x => x.Embed = eb.Build());
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task GiveawayEnd(kwum id)
        {
           var success = await _service.EndGiveawayAsync(ctx.Guild.Id, id);

           if(!success)
           {
               await Response().Error(strs.giveaway_not_found).SendAsync();
               return;
           }

           await ctx.OkAsync();
            _ = ctx.Message.DeleteAfter(5);
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task GiveawayReroll(kwum id)
        { 
            var success = await _service.RerollGiveawayAsync(ctx.Guild.Id, id);
            if (!success)
            {
                await Response().Error(strs.giveaway_not_found).SendAsync();
                return;
            }
            
            
            await ctx.OkAsync();
            _ = ctx.Message.DeleteAfter(5);
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task GiveawayCancel(kwum id)
        {
            var success = await _service.CancelGiveawayAsync(ctx.Guild.Id, id);

            if (!success)
            {
                await Response().Error(strs.giveaway_not_found).SendAsync();
                return;
            }

            await Response().Confirm(strs.giveaway_cancelled).SendAsync();
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task GiveawayList()
        {
            var giveaways = await _service.GetGiveawaysAsync(ctx.Guild.Id);

            if (!giveaways.Any())
            {
                await Response().Error(strs.no_givaways).SendAsync();
                return;
            }

            var eb = CreateEmbed()
                .WithTitle(GetText(strs.giveaway_list))
                .WithOkColor();

            foreach (var g in giveaways)
            {
                eb.AddField($"id:  {new kwum(g.Id)}", g.Message, true);
            }

            await Response().Embed(eb).SendAsync();
        }
    }
}