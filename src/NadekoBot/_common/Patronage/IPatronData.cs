namespace NadekoBot.Modules.Patronage;

public interface ISubscriberData
{
    public string UniquePlatformUserId { get; }
    public ulong UserId { get; }
    public int Cents { get; }
    
    public DateTime? LastCharge { get; }
    public SubscriptionChargeStatus ChargeStatus { get; }
}