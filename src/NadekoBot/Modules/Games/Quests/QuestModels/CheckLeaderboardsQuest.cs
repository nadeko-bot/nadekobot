namespace NadekoBot.Modules.Games.Quests;

public sealed class CheckLeaderboardsQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.CheckBetting;

    public string Name
        => "Leaderboard Enthusiast";

    public string Desc
        => "Check lb, xplb, fishlb and waifulb";

    public string ProgDesc
        => "";

    public QuestEventType EventType
        => QuestEventType.CommandUsed;

    public long RequiredAmount
        => 0b1111;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        if (!metadata.TryGetValue("name", out var name))
            return oldProgress;

        var progress = oldProgress;

        if (name == "leaderboard")
            progress |= 0b0001;
        else if (name == "xpleaderboard")
            progress |= 0b0010;
        else if (name == "waifulb")
            progress |= 0b0100;
        else if (name == "fishlb")
            progress |= 0b1000;

        return progress;
    }

    public string ToString(long progress)
    {
        var msg = "";

        var emoji = IQuest.INCOMPLETE;
        if ((progress & 0b0001) == 0b0001)
            emoji = IQuest.COMPLETED;

        msg += emoji + " flower lb seen\n";

        emoji = IQuest.INCOMPLETE;
        if ((progress & 0b0010) == 0b0010)
            emoji = IQuest.COMPLETED;
            
        msg += emoji + " xp lb seen\n";
        
        emoji = IQuest.INCOMPLETE;
        if ((progress & 0b0100) == 0b0100)
            emoji = IQuest.COMPLETED;
            
        msg += emoji + " waifu lb seen";
        
        emoji = IQuest.INCOMPLETE;
        if ((progress & 0b1000) == 0b1000)
            emoji = IQuest.COMPLETED;
            
        msg += "\n" + emoji + " fish lb seen";
        
        return msg;
    }
}