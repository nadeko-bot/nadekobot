#nullable disable
namespace NadekoBot.Modules.Patronage;

public sealed class PatreonMemberData : ISubscriberData
{
    public string PatreonUserId { get; init; }
    public ulong UserId { get; init; }
    public DateTime? LastChargeDate { get; init; }
    public string LastChargeStatus { get; init; }
    public int EntitledToCents { get; init; }

    public string UniquePlatformUserId
        => PatreonUserId;
    ulong ISubscriberData.UserId
        => UserId;
    public int Cents
        => EntitledToCents;
    public DateTime? LastCharge
        => LastChargeDate;
    public SubscriptionChargeStatus ChargeStatus
        => LastChargeStatus switch
        {
            "Paid" => SubscriptionChargeStatus.Paid,
            "Fraud" or "Refunded" => SubscriptionChargeStatus.Refunded,
            "Declined" or "Pending" => SubscriptionChargeStatus.Unpaid,
            _ => SubscriptionChargeStatus.Other,
        };
}