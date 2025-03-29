namespace NadekoBot.Modules.Games.Quests;

public sealed class SetPixelsQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.SetPixels;

    public string Name
        => "Pixel Artist";

    public string Desc
        => "Set 3 pixels";

    public string ProgDesc
        => "pixels set";

    public QuestEventType EventType
        => QuestEventType.PixelSet;

    public long RequiredAmount
        => 3;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        return oldProgress + 1;
    }
}