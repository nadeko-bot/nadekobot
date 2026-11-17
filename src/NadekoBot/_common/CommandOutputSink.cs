namespace NadekoBot.Modules.Administration;

// IsError is true for a message which the command sent as an error (red embed).
public readonly record struct CommandOutput(IUserMessage Message, bool IsError);

public readonly record struct CommandOutputSnapshot(CommandOutput[] Messages, string[] Reactions);

public sealed class CommandOutputSink
{
    // A command which posts more than this is a game or a list, and the first messages tell the agent enough.
    private const int MAX_MESSAGES = 10;
    private const int MAX_REACTIONS = 5;

    private readonly Lock _lock = new();
    private readonly List<CommandOutput> _messages = [];
    private readonly List<string> _reactions = [];
    private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;

    public Task FirstOutput
        => _first.Task;

    public void Add(IUserMessage message, bool isError)
    {
        lock (_lock)
        {
            if (_closed || _messages.Count >= MAX_MESSAGES)
                return;

            _messages.Add(new(message, isError));
        }

        _first.TrySetResult();
    }

    // Many commands answer only with a reaction on the member's message, for example a check mark.
    public void AddReaction(IEmote emote)
    {
        lock (_lock)
        {
            if (_closed || _reactions.Count >= MAX_REACTIONS)
                return;

            _reactions.Add(emote.Name);
        }

        _first.TrySetResult();
    }

    // The agent stops reading here. A command which runs on, for example a game, keeps its later messages to itself.
    public CommandOutputSnapshot Close()
    {
        lock (_lock)
        {
            _closed = true;
            return new(_messages.ToArray(), _reactions.ToArray());
        }
    }
}
