namespace NadekoBot.Modules.Games.Hangman;

public sealed class HangmanSave
{
    public const int VERSION = 1;

    public int V { get; init; }
    public string? Word { get; init; }
    public string? ImageUrl { get; init; }
    public string? Category { get; init; }

    // letters of the word which members found, without the last one, which ends the game
    public string? Correct { get; init; }
    public string? Incorrect { get; init; }
}
