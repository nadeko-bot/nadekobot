#nullable disable
namespace NadekoBot.Modules.Searches.Services;

public sealed class DefineData
{
    public required string Definition { get; init; }
    public required string Example { get; init; }
    public required string WordType { get; init; }
    public required string Word { get; init; }
}