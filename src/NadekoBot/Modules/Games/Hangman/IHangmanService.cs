namespace NadekoBot.Modules.Games.Hangman;

public enum HangmanStartResult : byte
{
    Started,
    AlreadyRunning,
    LimitReached,
    NoTerm
}

public interface IHangmanService
{
    Task<(HangmanStartResult Result, HangmanGame.State? State)> StartHangmanAsync(
        ulong guildId,
        ulong channelId,
        string? category);

    ValueTask<bool> StopHangman(ulong channelId);
    IReadOnlyCollection<string> GetHangmanTypes();
    void SetLastMessage(ulong channelId, IUserMessage msg);
}
