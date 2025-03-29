namespace NadekoBot.Modules.Games.Quests;

public class QuestEvent
{
    public QuestEventType EventType { get; }
    public ulong UserId { get; }
    public Dictionary<string, object> Metadata { get; }

    public QuestEvent(QuestEventType eventType, ulong userId, Dictionary<string, object>? metadata = null)
    {
        EventType = eventType;
        UserId = userId;
        Metadata = metadata ?? new Dictionary<string, object>();
    }
}