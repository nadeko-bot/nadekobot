namespace NadekoBot.Modules.Games.Quests;

public sealed class CatchQualityQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.CatchFish;

    public string Name
        => "Master Angler";

    public string Desc
        => "Catch a fish or an item rated 3 stars or above.";

    public string ProgDesc
        => "3+ star fish caught";

    public QuestEventType EventType
        => QuestEventType.FishCaught;

    public long RequiredAmount
        => 1;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        if (metadata.TryGetValue("stars", out var quality)
            && int.TryParse(quality, out var q)
            && q >= 3)
            return oldProgress + 1;

        return oldProgress;
    }
}