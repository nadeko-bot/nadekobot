namespace NadekoBot.Modules.Games.Quests;

public sealed class GiftWaifuQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.WaifuGift;

    public string Name
        => "Generous Gifter";

    public string Desc
        => "Gift a waifu 2 times.";

    public string ProgDesc
        => "waifus gifted";

    public QuestEventType EventType
        => QuestEventType.WaifuGiftSent;

    public long RequiredAmount
        => 2;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        return oldProgress + 1;
    }
}