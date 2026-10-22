using System.Collections.Frozen;

namespace NadekoBot.Db.UserData;

public readonly record struct ColumnValue(string Column, string? Value);

// .purgeuser changes the rows where UserColumn holds the user's id. Without Set, it deletes them.
public sealed record UserDataChange(string Table, string UserColumn, ColumnValue[]? Set = null, string? ExtraWhere = null);

// The purge removes names and the text which a user wrote for themselves.
// It keeps every record which other members, limits, punishments or the economy depend on,
// so a purge cannot free a member from a claim, a ban, a cooldown or a debt.
public static class UserDataColumns
{
    // Not the "??" marker of an unknown name, which .cacheusers fills in again.
    public const string NAME = "[deleted]";

    // A gift stores the sender's name and message on the sender's row (negative amount) and on the
    // recipient's row (positive amount). An award or a take stores them on the row of the other member.
    private const string SENT_BY_USER = "(\"Type\" = 'gift' AND \"Amount\" > 0) OR \"Type\" IN ('award', 'take')";
    private const string OWN_GIFT = "\"Type\" = 'gift' AND \"Amount\" < 0";

    // Columns which link a row to a member.
    public static readonly FrozenSet<string> UserColumns = FrozenSet.ToFrozenSet(
    [
        "UserId", "OtherId", "AuthorId", "OwnerId", "ManagerUserId", "WaifuUserId"
    ]);

    public static readonly UserDataChange[] Changes =
    [
        new("DiscordUser", "UserId", [new("Username", NAME), new("AvatarId", null)]),
        new("GiveawayUser", "UserId", [new("Name", NAME)]),
        new("StreamRoleBlacklistedUser", "UserId", [new("Username", NAME)]),
        new("StreamRoleWhitelistedUser", "UserId", [new("Username", NAME)]),
        new("Quotes", "AuthorId", [new("AuthorName", NAME)]),
        new("MusicPlaylists", "AuthorId", [new("Author", NAME)]),
        new("CurrencyTransactions", "OtherId", [new("Extra", NAME), new("Note", null)], SENT_BY_USER),
        new("CurrencyTransactions", "UserId", [new("Extra", NAME), new("Note", null)], OWN_GIFT),
        new("WaifuInfo", "UserId", [new("Description", null), new("Quote", null), new("CustomAvatarUrl", null)]),
        new("LineUpUser", "UserId", [new("Reason", null)]),
        new("Todos", "UserId"),
        new("TodosArchive", "UserId"),
        new("Reminders", "UserId"),
        // Commands which the user wrote, and which run as the user.
        new("ScheduledCommand", "UserId"),
    ];

    // Text columns on rows linked to a member which the purge keeps.
    public static readonly FrozenDictionary<string, string> Kept = new Dictionary<string, string>
    {
        ["AutoTranslateUsers.Source"] = "a language setting",
        ["AutoTranslateUsers.Target"] = "a language setting",
        ["Clubs.Name"] = "club content which the members share",
        ["Clubs.Description"] = "club content which the members share",
        ["Clubs.ImageUrl"] = "club content which the members share",
        ["Clubs.BannerUrl"] = "club content which the members share",
        ["CurrencyTransactions.Type"] = "the economy record",
        ["MusicPlaylists.Name"] = "a playlist which other members can play",
        ["NCPixel.Text"] = "a bought pixel which other members see",
        ["Patrons.UniquePlatformUserId"] = "a billing record",
        ["PlantedCurrency.Password"] = "a generated code",
        ["Quotes.Keyword"] = "server content",
        ["Quotes.Text"] = "server content",
        ["RewardedUsers.PlatformUserId"] = "a billing record, and without it the bot pays the reward again",
        ["ShopEntry.Name"] = "server content",
        ["ShopEntry.Command"] = "server content",
        ["ShopEntry.RoleName"] = "server content",
        ["StickyRoles.RoleIds"] = "the roles which the server gives back on a rejoin",
        ["Warnings.Reason"] = "a moderation record",
        // The moderator's name is part of the server's moderation log.
        ["Warnings.Moderator"] = "a moderation record",
        ["Warnings.ForgivenBy"] = "a moderation record",
        ["XpShopOwnedItem.ItemKey"] = "a purchase",
        ["UserNotifyBlock.Type"] = "a notification setting",
    }.ToFrozenDictionary();
}
