#nullable disable
namespace NadekoBot.Modules.Patronage;

/// <summary>
/// Services implementing this interface are handling pledges/subscriptions/payments coming
/// from a payment platform.
/// </summary>
public interface ISubscriptionHandler
{
    /// <summary>
    /// Get Current patrons in batches.
    /// This will only return patrons who have their discord account connected
    /// </summary>
    /// <returns>Batched patrons</returns>
    public IAsyncEnumerable<IReadOnlyCollection<ISubscriberData>> GetPatronsAsync();
}