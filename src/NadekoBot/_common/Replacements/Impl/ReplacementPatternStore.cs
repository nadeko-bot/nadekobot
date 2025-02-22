using System.Text.RegularExpressions;
using OneOf;

namespace NadekoBot.Common;

public sealed partial class ReplacementPatternStore : IReplacementPatternStore, INService
{
    private readonly ConcurrentDictionary<Guid, OneOf<string, Regex>> _guids = new();

    private readonly ConcurrentDictionary<string, ReplacementInfo> _defaultReplacements = new();
    private readonly ConcurrentDictionary<string, RegexReplacementInfo> _regexReplacements = new();

    public IReadOnlyDictionary<string, ReplacementInfo> Replacements
        => _defaultReplacements.AsReadOnly();

    public IReadOnlyDictionary<string, RegexReplacementInfo> RegexReplacements
        => _regexReplacements.AsReadOnly();

    public ReplacementPatternStore()
    {
        WithClient();
        WithChannel();
        WithServer();
        WithUsers();
        WithDefault();
        WithRegex();
    }

    // private async ValueTask<string> InternalReplace(string input, ReplacementContexta repCtx)
    // {
    //     // multiple executions vs single execution per replacement
    //     var minIndex = -1;
    //     var index = -1;
    //     foreach (var rep in _replacements)
    //     {
    //         while ((index = input.IndexOf(rep.Key, StringComparison.InvariantCulture)) != -1 && index > minIndex)
    //         {
    //             var valueToInsert = await rep.Value(repCtx);
    //             input = input[..index] + valueToInsert +input[(index + rep.Key.Length)..];
    //             minIndex = (index + valueToInsert.Length);
    //         }
    //     }
    //
    //     return input;
    // }

    private ValueTask<Guid?> InternalRegister(string token, Delegate repFactory)
    {
        if (!token.StartsWith('%') || !token.EndsWith('%'))
        {
            Log.Warning(
                """
                Invalid replacement token: {Token}
                Tokens have to start and end with a '%', ex: %mytoken%
                """,
                token);
            return new(default(Guid?));
        }

        if (_defaultReplacements.TryAdd(token, new ReplacementInfo(token, repFactory)))
        {
            var guid = Guid.NewGuid();
            _guids[guid] = token;
            return new(guid);
        }

        return new(default(Guid?));
    }

    public ValueTask<Guid?> Register(string token, Func<ValueTask<string>> repFactory)
        => InternalRegister(token, repFactory);

    public ValueTask<Guid?> Register<T1>(string token, Func<T1, ValueTask<string>> repFactory)
        => InternalRegister(token, repFactory);

    public ValueTask<Guid?> Register<T1, T2>(string token, Func<T1, T2, ValueTask<string>> repFactory)
        => InternalRegister(token, repFactory);

    public ValueTask<Guid?> Register(string token, Func<string> repFactory)
        => InternalRegister(token, () => new ValueTask<string>(repFactory()));

    public ValueTask<Guid?> Register<T1>(string token, Func<T1, string> repFactory)
        => InternalRegister(token, (T1 a) => new ValueTask<string>(repFactory(a)));

    public ValueTask<Guid?> Register<T1, T2>(string token, Func<T1, T2, string> repFactory)
        => InternalRegister(token, (T1 a, T2 b) => new ValueTask<string>(repFactory(a, b)));


    private ValueTask<Guid?> InternalRegexRegister(Regex regex, Delegate repFactory)
    {
        var regexPattern = regex.ToString();
        if (!regexPattern.StartsWith('%') || !regexPattern.EndsWith('%'))
        {
            Log.Warning(
                """
                Invalid replacement pattern: {Token}
                Tokens have to start and end with a '%', ex: %mytoken%
                """,
                regex);
            return new(default(Guid?));
        }

        if (_regexReplacements.TryAdd(regexPattern, new RegexReplacementInfo(regex, repFactory)))
        {
            var guid = Guid.NewGuid();
            _guids[guid] = regex;
            return new(guid);
        }

        return new(default(Guid?));
    }

    public ValueTask<Guid?> Register(Regex regex, Func<Match, string> repFactory)
        => InternalRegexRegister(regex, () => (Match m) => new ValueTask<string>(repFactory(m)));

    public ValueTask<Guid?> Register<T1>(Regex regex, Func<Match, T1, string> repFactory)
        => InternalRegexRegister(regex, (T1 a) => (Match m) => new ValueTask<string>(repFactory(m, a)));

    public bool Unregister(Guid guid)
    {
        if (_guids.TryRemove(guid, out var pattern))
        {
            return pattern.Match(
                token => _defaultReplacements.TryRemove(token, out _),
                regex => _regexReplacements.TryRemove(regex.ToString(), out _));
        }

        return false;
    }
}