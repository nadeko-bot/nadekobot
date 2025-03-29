namespace NadekoBot.Modules.Games.Quests;

public class QuestCommands : NadekoModule<QuestService>
{
    [Cmd]
    public async Task QuestLog()
    {
        var now = DateTime.UtcNow;
        var quests = await _service.GetUserQuestsAsync(ctx.User.Id, now);

        var embed = CreateEmbed()
            .WithOkColor()
            .WithTitle(GetText(strs.quest_log));

        var allDone = quests.All(x => x.UserQuest.IsCompleted);

        var tmrw = now.AddDays(1).Date;
        var desc = GetText(strs.dailies_reset(TimestampTag.FromDateTime(tmrw, TimestampTagStyles.Relative)));
        if (allDone)
            desc = GetText(strs.dailies_done) + "\n" + desc;

        embed.WithDescription(desc);

        foreach (var res in quests)
        {
            if (res.Quest is null)
                continue;

            embed.AddField(
                (res.UserQuest.IsCompleted ? IQuest.COMPLETED : IQuest.INCOMPLETE) + " " + res.Quest.Name,
                $"{res.Quest.Desc}\n\n" +
                res.Quest.ToString(res.UserQuest.Progress),
                true);
        }

        await Response().Embed(embed).SendAsync();
    }
}