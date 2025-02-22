namespace NadekoBot.Modules.Xp.Services;

public enum BuyResult
{
    Success,
    XpShopDisabled,
    AlreadyOwned,
    InsufficientFunds,
    UnknownItem,
    InsufficientPatronTier,
}