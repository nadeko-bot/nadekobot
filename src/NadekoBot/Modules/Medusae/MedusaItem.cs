namespace NadekoBot.Modules;

public sealed class ModuleItem
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Command { get; init; }
}