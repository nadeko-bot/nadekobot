namespace NadekoBot.Modules.Games.Common.Trivia;

public sealed class TriviaSave
{
    public const int VERSION = 1;

    public int V { get; init; }
    public bool Pokemon { get; init; }
    public bool NoHint { get; init; }
    public int WinRequirement { get; init; }
    public int QuestionTimer { get; init; }
    public int Timeout { get; init; }
    public int Inactivity { get; init; }
    public bool Stopped { get; init; }
    public TriviaScoreSave[]? Scores { get; init; }

    // the open question, which the game asks again after a restart
    public TriviaQuestionModel? Question { get; init; }
}

public sealed class TriviaScoreSave
{
    public ulong UserId { get; init; }
    public int Points { get; init; }
}
