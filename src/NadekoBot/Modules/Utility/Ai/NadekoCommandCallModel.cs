namespace NadekoBot.Modules.Utility;

public sealed class NadekoCommandCallModel
{
    public required string Name { get; set; }
    public required IReadOnlyList<string> Arguments { get; set; }
    public required string Remaining { get; set; }
}