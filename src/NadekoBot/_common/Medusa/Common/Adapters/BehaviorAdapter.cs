using NadekoBot.Medusa;

[DIIgnore]
public sealed class BehaviorAdapter : ICustomBehavior
{
    private readonly WeakReference<Snek> _snekWr;
    private readonly IMedusaStrings _strings;
    private readonly IServiceProvider _services;
    private readonly string _name;

    public string Name => _name;

    // unused
    public int Priority
        => 0;

    public BehaviorAdapter(WeakReference<Snek> snekWr, IMedusaStrings strings, IServiceProvider services)
    {
        _snekWr = snekWr;
        _strings = strings;
        _services = services;

        _name = snekWr.TryGetTarget(out var snek)
            ? $"snek/{snek.GetType().Name}"
            : "unknown";
    }

    public ValueTask<bool> ExecPreCommandAsync(ICommandContext context, string moduleName, CommandInfo command)
    {
        if (!_snekWr.TryGetTarget(out var snek))
            return new(false);

        return snek.ExecPreCommandAsync(ContextAdapterFactory.CreateNew(context, _strings, _services),
            moduleName,
            command.Name);
    }

    public ValueTask<bool> ExecOnMessageAsync(IGuild? guild, IUserMessage msg)
    {
        if (!_snekWr.TryGetTarget(out var snek))
            return new(false);

        return snek.ExecOnMessageAsync(guild, msg);
    }

    public ValueTask<string?> TransformInput(
        IGuild? guild,
        IMessageChannel channel,
        IUser user,
        string input)
    {
        if (!_snekWr.TryGetTarget(out var snek))
            return new((string?)null);

        return snek.ExecInputTransformAsync(guild, channel, user, input);
    }

    public ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (!_snekWr.TryGetTarget(out var snek))
            return default;

        return snek.ExecOnNoCommandAsync(guild, msg);
    }

    public async ValueTask ExecPostCommandAsync(ICommandContext context, string moduleName, string commandName)
    {
        if (!_snekWr.TryGetTarget(out var snek))
            return;

        await snek.ExecPostCommandAsync(ContextAdapterFactory.CreateNew(context, _strings, _services),
            moduleName,
            commandName);
    }

    public override string ToString()
        => _name;
}