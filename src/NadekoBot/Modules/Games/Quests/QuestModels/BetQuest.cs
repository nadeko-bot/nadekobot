namespace NadekoBot.Modules.Games.Quests;

public sealed class BetQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.Bet;

    public string Name
        => "High Roller";

    public string Desc
        => "Place 20 bets";

    public string ProgDesc
        => "bets placed";

    public QuestEventType EventType
        => QuestEventType.BetPlaced;

    public long RequiredAmount
        => 20;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        return oldProgress + 1;
    }
}