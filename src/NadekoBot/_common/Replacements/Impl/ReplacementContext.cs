using System.Text.RegularExpressions;

namespace NadekoBot.Common;

public sealed class ReplacementContext
{
    public DiscordSocketClient? Client { get; }
    public IGuild? Guild { get; }
    public IMessageChannel? Channel { get; }
    public IUser? User { get; }

    private readonly List<ReplacementInfo> _overrides = new();
    private readonly HashSet<string> _tokens = new();

    public IReadOnlyList<ReplacementInfo> Overrides
        => _overrides.AsReadOnly();

    private readonly List<RegexReplacementInfo> _regexOverrides = new();
    private readonly HashSet<string> _regexPatterns = new();

    public IReadOnlyList<RegexReplacementInfo> RegexOverrides
        => _regexOverrides.AsReadOnly();

    public ReplacementContext(ICommandContext cmdContext)
        : this(cmdContext.Client as DiscordSocketClient,
            cmdContext.Guild,
            cmdContext.Channel,
            cmdContext.User)
    {
    }

    public ReplacementContext(
        DiscordSocketClient? client = null,
        IGuild? guild = null,
        IMessageChannel? channel = null,
        IUser? user = null)
    {
        Client = client;
        Guild = guild;
        Channel = channel;
        User = user;
    }

    public ReplacementContext WithOverride(string key, Func<ValueTask<string>> repFactory)
    {
        if (_tokens.Add(key))
        {
            _overrides.Add(new(key, repFactory));
        }

        return this;
    }

    public ReplacementContext WithOverride(string key, Func<string> repFactory)
        => WithOverride(key, () => new ValueTask<string>(repFactory()));

    public ReplacementContext WithOverride(Regex regex, Func<Match, ValueTask<string>> repFactory)
    {
        if (_regexPatterns.Add(regex.ToString()))
        {
            _regexOverrides.Add(new(regex, repFactory));
        }

        return this;
    }

    public ReplacementContext WithOverride(Regex regex, Func<Match, string> repFactory)
        => WithOverride(regex, (Match m) => new ValueTask<string>(repFactory(m)));
}