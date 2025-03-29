namespace NadekoBot.Modules.Games.Quests;

public sealed class CatchTrashQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.CatchFish;

    public string Name
        => "Environmentalist";

    public string Desc
        => "Catch 10 trash items while fishing";

    public string ProgDesc
        => "items caught";

    public QuestEventType EventType
        => QuestEventType.FishCaught;

    public long RequiredAmount
        => 10;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        if (metadata.TryGetValue("type", out var type) && type == "trash")
            return oldProgress + 1;

        return oldProgress;
    }
}