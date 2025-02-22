namespace NadekoBot.Modules.Patronage;

/// <summary>
/// Represents information about why the user has triggered a quota limit
/// </summary>
public readonly struct QuotaLimit
{
    /// <summary>
    /// Amount of usages reached, which is the limit
    /// </summary>
    public int Quota { get; init; }
    
    /// <summary>
    /// Which period is this quota limit for (hourly, daily, monthly, etc...)
    /// </summary>
    public QuotaPer QuotaPeriod { get; init; }
    
    public QuotaLimit(int quota, QuotaPer quotaPeriod)
    {
        Quota = quota;
        QuotaPeriod = quotaPeriod;
    }
}