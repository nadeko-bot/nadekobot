#nullable disable
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace NadekoBot.Services;

// public sealed record class CommandStrings
// {
//     [YamlMember(Alias = "desc")]
//     public string Desc { get; set; }
//
//     [YamlMember(Alias = "args")]
//     public string[] Args { get; set; }
// }

public sealed record class CommandStrings
{
    [YamlMember(Alias = "desc")]
    public string Desc { get; set; }

    [YamlMember(Alias = "ex")]
    public string[] Examples { get; set; }
    
    [YamlMember(Alias = "params")]
    public Dictionary<string, CommandStringParam>[] Params { get; set; }
}

public sealed record class CommandStringParam
{
    // [YamlMember(Alias = "type", ScalarStyle = ScalarStyle.DoubleQuoted)]
    // public string Type { get; set; }
    
    [YamlMember(Alias = "desc", ScalarStyle = ScalarStyle.DoubleQuoted)]
    public string Desc{ get; set; }
}