namespace NadekoBot.Modules.Owner.Dangerous;

/// <summary>
/// Interface for the cleanup service that handles guild data cleanup.
/// </summary>
public interface ICleanupService
{
    /// <summary>
    /// Deletes all guild data from the database for guilds the bot is no longer a member of.
    /// </summary>
    Task<KeepResult?> DeleteMissingGuildDataAsync();
}
