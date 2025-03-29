namespace NadekoBot.Modules.Games.Quests;

public sealed class HangmanWinQuest : IQuest
{
    public QuestIds QuestId
        => QuestIds.HangmanWin;

    public string Name
        => "Hangman Champion";

    public string Desc
        => "Win 2 games of Hangman";

    public string ProgDesc
        => "hangman games won";

    public QuestEventType EventType
        => QuestEventType.GameWon;

    public long RequiredAmount
        => 2;

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress)
    {
        if (!metadata.TryGetValue("game", out var value))
            return oldProgress;

        return value == "hangman" ? oldProgress + 1 : oldProgress;
    }
}