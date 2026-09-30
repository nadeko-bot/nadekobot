#nullable disable
using CommandLine;
using System.Collections.Immutable;

namespace NadekoBot.Modules.Games.Common.Acrophobia;

public sealed class AcrophobiaGame : IDisposable
{
    public enum Phase
    {
        Submission,
        Voting,
        Ended
    }

    public enum UserInputResult
    {
        Submitted,
        SubmissionFailed,
        Voted,
        VotingFailed,
        Failed
    }

    public event Func<AcrophobiaGame, Task> OnStarted = delegate { return Task.CompletedTask; };

    public event Func<AcrophobiaGame, ImmutableArray<KeyValuePair<AcrophobiaUser, int>>, Task> OnVotingStarted =
        delegate { return Task.CompletedTask; };

    public event Func<string, Task> OnUserVoted = delegate { return Task.CompletedTask; };

    public event Func<AcrophobiaGame, ImmutableArray<KeyValuePair<AcrophobiaUser, int>>, Task> OnEnded = delegate
    {
        return Task.CompletedTask;
    };

    public Phase CurrentPhase { get; private set; } = Phase.Submission;
    public ImmutableArray<char> StartingLetters { get; private set; }
    public Options Opts { get; }

    private readonly Dictionary<AcrophobiaUser, int> _submissions = new();
    private readonly List<AcrophobiaUser> _order = [];
    private readonly SemaphoreSlim _locker = new(1, 1);
    private readonly NadekoRandom _rng;

    private readonly HashSet<ulong> _usersWhoVoted = [];

    public AcrophobiaGame(Options options)
    {
        Opts = options;
        _rng = new();
        InitializeStartingLetters();
    }

    public async Task Run()
    {
        await OnStarted(this);
        await Task.Delay(Opts.SubmissionTime * 1000);
        await _locker.WaitAsync();
        try
        {
            if (_submissions.Count == 0)
            {
                CurrentPhase = Phase.Ended;
                await OnVotingStarted(this, ImmutableArray.Create<KeyValuePair<AcrophobiaUser, int>>());
                return;
            }

            if (_submissions.Count == 1)
            {
                CurrentPhase = Phase.Ended;
                await OnVotingStarted(this, SnapshotInternal());
                return;
            }

            CurrentPhase = Phase.Voting;

            await OnVotingStarted(this, SnapshotInternal());
        }
        finally { _locker.Release(); }

        await Task.Delay(Opts.VoteTime * 1000);
        await _locker.WaitAsync();
        try
        {
            CurrentPhase = Phase.Ended;
            await OnEnded(this, SnapshotInternal());
        }
        finally { _locker.Release(); }
    }

    private ImmutableArray<KeyValuePair<AcrophobiaUser, int>> SnapshotInternal()
    {
        var builder = ImmutableArray.CreateBuilder<KeyValuePair<AcrophobiaUser, int>>(_order.Count);
        foreach (var user in _order)
            builder.Add(new(user, _submissions[user]));

        return builder.MoveToImmutable();
    }

    public static ImmutableArray<KeyValuePair<AcrophobiaUser, int>> GetWinners(
        ImmutableArray<KeyValuePair<AcrophobiaUser, int>> votes)
    {
        var max = 0;
        foreach (var vote in votes)
        {
            if (vote.Value > max)
                max = vote.Value;
        }

        if (max == 0)
            return [];

        var builder = ImmutableArray.CreateBuilder<KeyValuePair<AcrophobiaUser, int>>();
        foreach (var vote in votes)
        {
            if (vote.Value == max)
                builder.Add(vote);
        }

        return builder.ToImmutable();
    }

    private void InitializeStartingLetters()
    {
        var wordCount = _rng.Next(3, 6);

        var lettersArr = new char[wordCount];

        for (var i = 0; i < wordCount; i++)
        {
            var randChar = (char)_rng.Next(65, 91);
            lettersArr[i] = randChar == 'X' ? (char)_rng.Next(65, 88) : randChar;
        }

        StartingLetters = lettersArr.ToImmutableArray();
    }

    public async Task<bool> UserInput(ulong userId, string userName, string input)
    {
        var user = new AcrophobiaUser(userId, userName, input.ToLowerInvariant().ToTitleCase());

        await _locker.WaitAsync();
        try
        {
            switch (CurrentPhase)
            {
                case Phase.Submission:
                    if (_submissions.ContainsKey(user) || !IsValidAnswer(input))
                        break;

                    _submissions.Add(user, 0);
                    _order.Add(user);
                    return true;
                case Phase.Voting:
                    AcrophobiaUser toVoteFor;
                    if (!int.TryParse(input, out var index)
                        || --index < 0
                        || index >= _order.Count
                        || (toVoteFor = _order[index]).UserId == user.UserId
                        || !_usersWhoVoted.Add(userId))
                        break;
                    ++_submissions[toVoteFor];
                    _ = Task.Run(() => OnUserVoted(userName));
                    return true;
            }

            return false;
        }
        finally
        {
            _locker.Release();
        }
    }

    private bool IsValidAnswer(string input)
    {
        input = input.ToUpperInvariant();

        var inputWords = input.Split(' ');

        if (inputWords.Length
            != StartingLetters.Length) // number of words must be the same as the number of the starting letters
            return false;

        for (var i = 0; i < StartingLetters.Length; i++)
        {
            var letter = StartingLetters[i];

            if (!inputWords[i]
                    .StartsWith(letter.ToString(), StringComparison.InvariantCulture)) // all first letters must match
                return false;
        }

        return true;
    }

    public void Dispose()
    {
        CurrentPhase = Phase.Ended;
        OnStarted = null;
        OnEnded = null;
        OnUserVoted = null;
        OnVotingStarted = null;
        _usersWhoVoted.Clear();
        _submissions.Clear();
        _order.Clear();
        _locker.Dispose();
    }

    public class Options : INadekoCommandOptions
    {
        [Option('s',
            "submission-time",
            Required = false,
            Default = 60,
            HelpText = "Time after which the submissions are closed and voting starts.")]
        public int SubmissionTime { get; set; } = 60;

        [Option('v',
            "vote-time",
            Required = false,
            Default = 30,
            HelpText = "Time after which the voting is closed and the winner is declared.")]
        public int VoteTime { get; set; } = 30;

        public void NormalizeOptions()
        {
            if (SubmissionTime is < 15 or > 300)
                SubmissionTime = 60;
            if (VoteTime is < 15 or > 120)
                VoteTime = 30;
        }
    }
}