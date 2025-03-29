namespace NadekoBot.Modules.Games.Quests;

public sealed class BetFlowersQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.Bet;

    public string Name
        => "Flower Gambler";

    public string Desc
        => "Bet 300 flowers";

    public string ProgDesc
        => "flowers bet";

    public QuestEventType EventType
        => QuestEventType.BetPlaced;

    public long RequiredAmount
        => 300;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        if (!metadata.TryGetValue("amount", out var amountStr)
            || !long.TryParse(amountStr, out var amount))
            return oldProgress;

        return oldProgress + amount;
    }
}