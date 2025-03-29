namespace NadekoBot.Modules.Patronage;

/// <summary>
/// Manages patrons and provides access to their data  
/// </summary>
public interface IPatronageService
{
    /// <summary>
    /// Called when the payment is made.
    /// Either as a single payment for that patron,
    /// or as a recurring monthly donation.
    /// </summary>
    public event Func<Patron, Task> OnNewPatronPayment;
    
    /// <summary>
    /// Called when the patron changes the pledge amount
    /// (Patron old, Patron new) => Task
    /// </summary>
    public event Func<Patron, Patron, Task> OnPatronUpdated;
    
    /// <summary>
    /// Called when the patron refunds the purchase or it's marked as fraud
    /// </summary>
    public event Func<Patron, Task> OnPatronRefunded;

    /// <summary>
    /// Gets a Patron with the specified userId
    /// </summary>
    /// <param name="userId">UserId for which to get the patron data for.</param>
    /// <returns>A patron with the specifeid userId</returns>
    public Task<Patron?> GetPatronAsync(ulong userId);
    
    Task<bool> LimitHitAsync(string name, ulong userId, int def);
    Task<int> GetUserLimit(string name, ulong userId, int def );
    
    PatronConfigData GetConfig();
    int PercentBonus(Patron? user);
    int PercentBonus(long amount);
}