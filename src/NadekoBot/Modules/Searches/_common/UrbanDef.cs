#nullable disable
namespace NadekoBot.Modules.Searches.Common;

public class UrbanResponse
{
    public UrbanDef[] List { get; set; }
}

public class UrbanDef
{
    public string Word { get; set; }
    public string Definition { get; set; }
    public string Permalink { get; set; }
}