namespace NadekoBot;

public sealed class NadekoInteractionGroup
{
    private readonly NadekoInteractionBase[] _children;

    public NadekoInteractionGroup(params NadekoInteractionBase[] children)
        => _children = children;

    public MessageComponent CreateComponent()
    {
        var cb = new ComponentBuilder();
        foreach (var child in _children)
            child.AddTo(cb);
        return cb.Build();
    }

    public async Task RunAsync(IUserMessage msg)
    {
        if (_children.Length == 0)
            return;

        if (_children.Length == 1)
        {
            await _children[0].RunAsync(msg);
            return;
        }

        var tasks = new Task[_children.Length];
        for (var i = 0; i < _children.Length; i++)
            tasks[i] = _children[i].RunAsync(msg);

        await Task.WhenAny(tasks);

        for (var i = 0; i < _children.Length; i++)
            _children[i].SetCompleted();

        await Task.WhenAll(tasks);
    }
}
