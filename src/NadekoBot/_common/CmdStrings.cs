#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Common;

public class CmdStrings
{
    public string[] Usages { get; }
    public string Description { get; }

    [JsonConstructor]
    public CmdStrings([JsonProperty("args")] string[] usages, [JsonProperty("desc")] string description)
    {
        Usages = usages;
        Description = description;
    }
}