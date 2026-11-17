#nullable disable
namespace NadekoBot.Modules.Help;

public sealed class CommandJsonObject
{
    public string[] Aliases { get; set; }
    public string Description { get; set; }
    public string[] Usage { get; set; }

    // One object for each overload, parameter name to description, in the order of the method's parameters.
    public Dictionary<string, string>[] Params { get; set; }

    public string Submodule { get; set; }
    public string Module { get; set; }
    public List<string> Options { get; set; }
    public string[] Requirements { get; set; }
}