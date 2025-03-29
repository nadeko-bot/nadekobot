namespace NadekoBot.Modules.Games.Quests;

public sealed class CatchFishQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.CatchFish;

    public string Name
        => "Fisherman";

    public string Desc
        => "Catch 10 fish";

    public string ProgDesc
        => "fish caught";

    public QuestEventType EventType
        => QuestEventType.FishCaught;

    public long RequiredAmount
        => 10;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        if (metadata.TryGetValue("type", out var type) && type == "fish")
            return oldProgress + 1;

        return oldProgress;
    }
}