using YamlDotNet.Serialization;

namespace NadekoBot.Medusa;

public readonly struct CommandStrings
{
    public CommandStrings(string? desc, string[]? args)
    {
        Desc = desc;
        Args = args;
    }

    [YamlMember(Alias = "desc")]
    public string? Desc { get; init; }
    
    [YamlMember(Alias = "args")]
    public string[]? Args { get; init; }

    public void Deconstruct(out string? desc, out string[]? args)
    {
        desc = Desc;
        args = Args;
    }
}