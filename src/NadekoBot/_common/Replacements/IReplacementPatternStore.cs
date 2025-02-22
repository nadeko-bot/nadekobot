using System.Text.RegularExpressions;

namespace NadekoBot.Common;

public interface IReplacementPatternStore : INService
{
    IReadOnlyDictionary<string, ReplacementInfo> Replacements { get; }
    IReadOnlyDictionary<string, RegexReplacementInfo> RegexReplacements { get; }

    ValueTask<Guid?> Register(string token, Func<ValueTask<string>> repFactory);
    ValueTask<Guid?> Register<T1>(string token, Func<T1, ValueTask<string>> repFactory);
    ValueTask<Guid?> Register<T1, T2>(string token, Func<T1, T2, ValueTask<string>> repFactory);
    
    ValueTask<Guid?> Register(string token, Func<string> repFactory);
    ValueTask<Guid?> Register<T1>(string token, Func<T1, string> repFactory);
    ValueTask<Guid?> Register<T1, T2>(string token, Func<T1, T2, string> repFactory);
    
    ValueTask<Guid?> Register(Regex regex, Func<Match, string> repFactory);
    ValueTask<Guid?> Register<T1>(Regex regex, Func<Match, T1, string> repFactory);
}