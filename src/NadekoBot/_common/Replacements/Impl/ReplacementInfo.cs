using System.Reflection;
using System.Text.RegularExpressions;

namespace NadekoBot.Common;

public sealed class ReplacementInfo
{
    private readonly Delegate _del;
    public IReadOnlyCollection<Type> InputTypes { get; }
    public string Token { get; }

    private static readonly Func<ValueTask<string?>> _falllbackFunc = static () => default;

    public ReplacementInfo(string token, Delegate del)
    {
        _del = del;
        InputTypes = del.GetMethodInfo().GetParameters().Select(x => x.ParameterType).ToArray().AsReadOnly();
        Token = token;
    }

    public async Task<string?> GetValueAsync(params object?[]? objs)
        => await (ValueTask<string?>)(_del.DynamicInvoke(objs) ?? _falllbackFunc);

    public override int GetHashCode()
        => Token.GetHashCode();

    public override bool Equals(object? obj)
        => obj is ReplacementInfo ri && ri.Token == Token;
}

public sealed class RegexReplacementInfo
{
    private readonly Delegate _del;
    public IReadOnlyCollection<Type> InputTypes { get; }

    public Regex Regex { get; }
    public string Pattern { get; }

    private static readonly Func<Match, ValueTask<string?>> _falllbackFunc = static _ => default;

    public RegexReplacementInfo(Regex regex, Delegate del)
    {
        _del = del;
        InputTypes = del.GetMethodInfo().GetParameters().Select(x => x.ParameterType).ToArray().AsReadOnly();
        Regex = regex;
        Pattern = Regex.ToString();
    }

    public async Task<string?> GetValueAsync(Match m, params object?[]? objs)
        => await ((Func<Match, ValueTask<string?>>)(_del.DynamicInvoke(objs) ?? _falllbackFunc))(m);

    public override int GetHashCode()
        => Regex.GetHashCode();

    public override bool Equals(object? obj)
        => obj is RegexReplacementInfo ri && ri.Pattern == Pattern;
}