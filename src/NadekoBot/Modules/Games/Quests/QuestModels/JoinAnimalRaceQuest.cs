namespace NadekoBot.Modules.Games.Quests;

public sealed class JoinAnimalRaceQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.JoinAnimalRace;

    public string Name
        => "Race Participant";

    public string Desc
        => "Join an animal race";

    public string ProgDesc
        => "races joined";
    
    public QuestEventType EventType
        => QuestEventType.RaceJoined;

    public long RequiredAmount
        => 1;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        return oldProgress + 1;
    }
}