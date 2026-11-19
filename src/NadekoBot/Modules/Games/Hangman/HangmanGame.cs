using NadekoBot.Modules.Games.SavedGames;

namespace NadekoBot.Modules.Games.Hangman;

public sealed class HangmanGame
{
    public const int MAX_ERRORS = 5;

    public enum GuessResult
    {
        NoAction,
        AlreadyTried,
        Incorrect,
        Guess,
        Win
    }

    public enum Phase
    {
        Running,
        Ended
    }

    private Phase CurrentPhase { get; set; }

    private readonly HashSet<char> _incorrect = new();
    private readonly HashSet<char> _correct = new();
    private readonly HashSet<char> _remaining;

    private readonly string _word;
    private readonly string _imageUrl;

    public string Category { get; }

    public int SavedGameId { get; set; }

    public ulong GuildId { get; set; }

    public HangmanGame(HangmanTerm term, string cat)
    {
        _word = term.Word;
        _imageUrl = term.ImageUrl;
        Category = cat;

        _remaining = _word.ToLowerInvariant().Where(x => char.IsLetter(x)).Select(char.ToLowerInvariant).ToHashSet();
    }

    public State GetState(GuessResult guessResult = GuessResult.NoAction)
        => new(
            Category,
            _incorrect.Count,
            CurrentPhase,
            CurrentPhase == Phase.Ended ? _word : GetScrambledWord(),
            guessResult,
            _incorrect.ToList(),
            CurrentPhase == Phase.Ended ? _imageUrl : string.Empty);

    private string GetScrambledWord()
    {
        Span<char> output = stackalloc char[_word.Length * 2];
        for (var i = 0; i < _word.Length; i++)
        {
            var ch = _word[i];
            if (ch == ' ')
                output[i * 2] = ' ';
            if (!char.IsLetter(ch) || !_remaining.Contains(char.ToLowerInvariant(ch)))
                output[i * 2] = ch;
            else
                output[i * 2] = '_';

            output[(i * 2) + 1] = ' ';
        }

        return new(output);
    }

    public State Guess(string guess)
    {
        if (CurrentPhase != Phase.Running)
            return GetState();

        var text = guess.AsSpan().Trim();
        if (text.IsEmpty)
            return GetState();

        if (text.Length > 1)
        {
            if (text.Equals(_word, StringComparison.InvariantCultureIgnoreCase))
            {
                CurrentPhase = Phase.Ended;
                return GetState(GuessResult.Win);
            }

            return GetState();
        }

        var charGuess = char.ToLowerInvariant(text[0]);
        if (!char.IsLetter(charGuess))
            return GetState();

        if (_incorrect.Contains(charGuess) || _correct.Contains(charGuess))
            return GetState(GuessResult.AlreadyTried);

        if (_remaining.Remove(charGuess))
        {
            if (_remaining.Count == 0)
            {
                CurrentPhase = Phase.Ended;
                return GetState(GuessResult.Win);
            }

            _correct.Add(charGuess);
            return GetState(GuessResult.Guess);
        }

        _incorrect.Add(charGuess);
        if (_incorrect.Count > MAX_ERRORS)
        {
            CurrentPhase = Phase.Ended;
            return GetState(GuessResult.Incorrect);
        }

        return GetState(GuessResult.Incorrect);
    }

    private HangmanSave ToSave()
        => new()
        {
            V = HangmanSave.VERSION,
            Word = _word,
            ImageUrl = _imageUrl,
            Category = Category,
            Correct = ToTextInternal(_correct),
            Incorrect = ToTextInternal(_incorrect)
        };

    private static string ToTextInternal(HashSet<char> letters)
        => string.Create(letters.Count,
            letters,
            static (span, set) =>
            {
                var i = 0;
                foreach (var ch in set)
                    span[i++] = ch;
            });

    public byte[] Save()
        => SavedGameJson.Write(ToSave(), SavedGameJsonContext.Default.HangmanSave);

    // null when the data is not a running game of this version
    public static HangmanGame? Load(byte[] data)
    {
        if (SavedGameJson.Read(data, SavedGameJsonContext.Default.HangmanSave)
            is not { V: HangmanSave.VERSION, Word: { Length: > 0 } word, Correct: { } correct, Incorrect: { } incorrect } save)
            return null;

        var game = new HangmanGame(new() { Word = word, ImageUrl = save.ImageUrl ?? string.Empty },
            save.Category ?? string.Empty);

        foreach (var ch in correct)
        {
            if (game._remaining.Remove(ch))
                game._correct.Add(ch);
        }

        foreach (var ch in incorrect)
            game._incorrect.Add(ch);

        if (game._remaining.Count == 0 || game._incorrect.Count > MAX_ERRORS)
            return null;

        return game;
    }

    public record State(
        string Category,
        int Errors,
        Phase Phase,
        string Word,
        GuessResult GuessResult,
        List<char> MissedLetters,
        string ImageUrl)
    {
        public bool Failed
            => Errors > MAX_ERRORS;
    }
}
