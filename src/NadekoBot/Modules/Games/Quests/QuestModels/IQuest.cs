using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Games.Quests;

public interface IQuest
{
    QuestIds QuestId { get; }
    string Name { get; }
    string Desc { get; }
    string ProgDesc { get; }
    QuestEventType EventType { get; }
    long RequiredAmount { get; }

    public long TryUpdateProgress(IDictionary<string, string> metadata, long oldProgress);

    public virtual string ToString(long progress)
        => GetEmoji(progress, RequiredAmount) + $" [{progress}/{RequiredAmount}] " + ProgDesc;

    public static string GetEmoji(long progress, long requiredAmount)
        => progress >= requiredAmount
            ? COMPLETED
            : INCOMPLETE;

    /// <summary>
    /// Completed Emoji
    /// </summary>
    public const string COMPLETED = "✅";

    /// <summary>
    /// Incomplete Emoji
    /// </summary>
    public const string INCOMPLETE = "❌";
}