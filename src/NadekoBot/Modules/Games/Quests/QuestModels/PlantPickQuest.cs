namespace NadekoBot.Modules.Games.Quests;

public sealed class PlantPickQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.PlantPick;

    public string Name
        => "Gardener";

    public string Desc
        => "pick and plant";

    public string ProgDesc
        => "";

    public QuestEventType EventType
        => QuestEventType.PlantOrPick;

    public long RequiredAmount
        => 0b11;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        if (!metadata.TryGetValue("type", out var val))
            return oldProgress;

        if (val == "plant")
        {
            oldProgress |= 0b10;
            return oldProgress;
        }

        if (val == "pick")
        {
            oldProgress |= 0b01;
            return oldProgress;
        }

        return oldProgress;
    }

    public string ToString(long progress)
    {
        var msg = "";

        var emoji = IQuest.INCOMPLETE;
        if ((progress & 0b01) == 0b01)
            emoji = IQuest.COMPLETED;

        msg += emoji + " picked flowers\n";

        emoji = IQuest.INCOMPLETE;
        if ((progress & 0b10) == 0b10)
            emoji = IQuest.COMPLETED;

        msg += emoji + " planted flowers";

        return msg;
    }
}
