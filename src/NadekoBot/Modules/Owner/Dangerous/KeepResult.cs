namespace NadekoBot.Modules.Owner.Dangerous;

/// <summary>
/// Result of the cleanup operation containing the count of remaining guilds.
/// </summary>
public sealed class KeepResult
{
    /// <summary>
    /// Number of guilds remaining in the database.
    /// </summary>
    public required int GuildCount { get; init; }
}
