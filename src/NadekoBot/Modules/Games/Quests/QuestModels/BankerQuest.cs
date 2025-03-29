namespace NadekoBot.Modules.Games.Quests;

public sealed class BankerQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.BankDeposit;

    public string Name
        => "Banker";

    public string Desc
        => "Perform bank actions";

    public string ProgDesc
        => "";

    public QuestEventType EventType
        => QuestEventType.BankAction;

    public long RequiredAmount
        => 0b111;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        if (!metadata.TryGetValue("type", out var type))
            return oldProgress;

        var progress = oldProgress;

        if (type == "balance")
            progress |= 0b001;
        else if (type == "deposit")
            progress |= 0b010;
        else if (type == "withdraw")
            progress |= 0b100;

        return progress;
    }

    public string ToString(long progress)
    {
        var msg = "";

        var emoji = IQuest.INCOMPLETE;
        if ((progress & 0b001) == 0b001)
            emoji = IQuest.COMPLETED;

        msg += emoji + " checked bank balance";

        emoji = IQuest.INCOMPLETE;
        if ((progress & 0b010) == 0b010)
            emoji = IQuest.COMPLETED;

        msg += "\n" + emoji + " made a deposit";

        emoji = IQuest.INCOMPLETE;
        if ((progress & 0b100) == 0b100)
            emoji = IQuest.COMPLETED;

        msg += "\n" + emoji + " made a withdrawal";

        return msg;
    }

}