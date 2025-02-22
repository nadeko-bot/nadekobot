namespace NadekoBot.Medusa;

public sealed record SnekInfo(
    string Name,
    SnekInfo? Parent,
    Snek Instance,
    IReadOnlyCollection<SnekCommandData> Commands,
    IReadOnlyCollection<FilterAttribute> Filters)
{
    public List<SnekInfo> Subsneks { get; set; } = new();
}