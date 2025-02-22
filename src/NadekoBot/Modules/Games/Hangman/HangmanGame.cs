#nullable disable

namespace NadekoBot.Modules.Games.Hangman;

public sealed class HangmanGame
{
    public enum GuessResult { NoAction, AlreadyTried, Incorrect, Guess, Win }

    public enum Phase { Running, Ended }

    private Phase CurrentPhase { get; set; }

    private readonly HashSet<char> _incorrect = new();
    private readonly HashSet<char> _correct = new();
    private readonly HashSet<char> _remaining = new();

    private readonly string _word;
    private readonly string _imageUrl;

    public HangmanGame(HangmanTerm term)
    {
        _word = term.Word;
        _imageUrl = term.ImageUrl;

        _remaining = _word.ToLowerInvariant().Where(x => char.IsLetter(x)).Select(char.ToLowerInvariant).ToHashSet();
    }

    public State GetState(GuessResult guessResult = GuessResult.NoAction)
        => new(_incorrect.Count,
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
                output[i * 2] = ' ';
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

        guess = guess.Trim();
        if (guess.Length > 1)
        {
            if (guess.Equals(_word, StringComparison.InvariantCultureIgnoreCase))
            {
                CurrentPhase = Phase.Ended;
                return GetState(GuessResult.Win);
            }

            return GetState();
        }

        var charGuess = guess[0];
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
        if (_incorrect.Count > 5)
        {
            CurrentPhase = Phase.Ended;
            return GetState(GuessResult.Incorrect);
        }

        return GetState(GuessResult.Incorrect);
    }

    public record State(
        int Errors,
        Phase Phase,
        string Word,
        GuessResult GuessResult,
        List<char> MissedLetters,
        string ImageUrl)
    {
        public bool Failed
            => Errors > 5;
    }
}