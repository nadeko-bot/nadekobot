using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

/// <summary>
/// Notification model for when a user fishes out currency.
/// </summary>
public readonly record struct FishCurrencyNotifyModel(
    ulong UserId,
    string Amount
) : INotifyModel<FishCurrencyNotifyModel>
{
    public const string PH_USER = "user";
    public const string PH_AMOUNT = "amount";

    public static string KeyName
        => "notify.fishcurrency";

    public static NotifyType NotifyType
        => NotifyType.FishCurrency;

    public static IReadOnlyList<NotifyModelPlaceholderData<FishCurrencyNotifyModel>> GetReplacements()
        =>
        [
            new(PH_USER, static (data, g) => g.GetUser(data.UserId)?.ToString() ?? data.UserId.ToString()),
            new(PH_AMOUNT, static (data, _) => data.Amount),
        ];

    public bool TryGetUserId(out ulong userId)
    {
        userId = UserId;
        return true;
    }
}
