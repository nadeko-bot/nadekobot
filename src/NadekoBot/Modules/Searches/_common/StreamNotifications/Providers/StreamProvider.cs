using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

/// <summary>
///     Abstract class implemented by providers of all supported platforms
/// </summary>
public abstract class Provider
{
    /// <summary>
    ///     Type of the platform.
    /// </summary>
    public abstract FollowedStream.FType Platform { get; }

    /// <summary>
    ///     Gets the stream usernames which fail to execute due to an error, and when they started throwing errors.
    ///     This can happen if stream name is invalid, or if the stream doesn't exist anymore.
    ///     Override to provide a custom implementation
    /// </summary>
    public virtual IReadOnlyDictionary<string, DateTime> FailingStreams
        => _failingStreams;

    /// <summary>
    ///     When was the first time the stream continually had errors while being retrieved
    /// </summary>
    protected readonly ConcurrentDictionary<string, DateTime> _failingStreams = new();

    /// <summary>
    ///     Checks whether the specified url is a valid stream url for this platform.
    /// </summary>
    /// <param name="url">Url to check</param>
    /// <returns>True if valid, otherwise false</returns>
    public abstract Task<bool> IsValidUrl(string url);

    /// <summary>
    ///     Gets stream data of the stream on the specified url on this <see cref="Platform" />
    /// </summary>
    /// <param name="url">Url of the stream</param>
    /// <returns><see cref="StreamData" /> of the specified stream. Null if none found</returns>
    public abstract Task<StreamData?> GetStreamDataByUrlAsync(string url);

    /// <summary>
    ///     Gets stream data of the specified id/username on this <see cref="Platform" />
    /// </summary>
    /// <param name="login">Name (or id where applicable) of the user on the platform</param>
    /// <returns><see cref="StreamData" /> of the user. Null if none found</returns>
    public abstract Task<StreamData?> GetStreamDataAsync(string login);

    /// <summary>
    ///     Gets stream data of all specified ids/usernames on this <see cref="Platform" />
    /// </summary>
    /// <param name="usernames">List of ids/usernames</param>
    /// <returns><see cref="StreamData" /> of all users, in the same order. Null for every id/user not found.</returns>
    public abstract Task<IReadOnlyCollection<StreamData>> GetStreamDataAsync(List<string> usernames);

    /// <summary>
    /// Unmark the stream as errored. You should override this method
    /// if you've overridden the <see cref="FailingStreams"/> property.
    /// </summary>
    /// <param name="login"></param>
    public virtual void ClearErrorsFor(string login)
        => _failingStreams.Clear();
}