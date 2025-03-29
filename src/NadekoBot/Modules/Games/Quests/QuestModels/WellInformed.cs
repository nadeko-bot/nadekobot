namespace NadekoBot.Modules.Games.Quests;

public sealed class WellInformedQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.WellInformed;

    public string Name
        => "Well Informed";

    public string Desc
        => "Check your flower stats";

    public string ProgDesc
        => "";

    public QuestEventType EventType
        => QuestEventType.CommandUsed;

    public long RequiredAmount
        => 0b111;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        if (!metadata.TryGetValue("name", out var type))
            return oldProgress;

        var progress = oldProgress;

        if (type == "cash")
            progress |= 0b001;
        else if (type == "rakeback")
            progress |= 0b010;
        else if (type == "betstats")
            progress |= 0b100;

        return progress;
    }

    public string ToString(long progress)
    {
        var msg = "";

        var emoji = IQuest.INCOMPLETE;
        if ((progress & 0b001) == 0b001)
            emoji = IQuest.COMPLETED;

        msg += emoji + " checked cash\n";

        emoji = IQuest.INCOMPLETE;
        if ((progress & 0b010) == 0b010)
            emoji = IQuest.COMPLETED;

        msg += emoji + " checked rakeback\n";

        emoji = IQuest.INCOMPLETE;
        if ((progress & 0b100) == 0b100)
            emoji = IQuest.COMPLETED;

        msg += emoji + " checked bet stats";

        return msg;
    }
}