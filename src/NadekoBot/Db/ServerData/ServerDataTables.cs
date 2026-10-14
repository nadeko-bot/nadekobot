using System.Collections.Frozen;

namespace NadekoBot.Db.ServerData;

// A column which holds the Id of a row in another exported table.
public readonly record struct ParentRef(string Column, string Parent);

public sealed record ServerDataTable(
    string Name,
    ParentRef? Owner = null,
    ParentRef[]? Refs = null,
    string? ExtraWhere = null,
    string[]? NullOnExport = null,
    bool HasIdentity = true,
    bool HasGuildId = true,
    bool IsXp = false);

public static class ServerDataTables
{
    public const string XP_TABLE = "UserXpStats";
    public const string HISTORY_TABLE = "__EFMigrationsHistory";
    public const string INDEX_LIST_TABLE = "__ServerExportIndex";
    public const string GUILD_COLUMN = "GuildId";
    public const string ID_COLUMN = "Id";

    // Parents come before their children.
    public static readonly ServerDataTable[] All =
    [
        new("GuildConfigs"),
        new("Permissions", Owner: new("GuildConfigId", "GuildConfigs")),
        new("StreamRoleSettings", Refs: [new("GuildConfigId", "GuildConfigs")]),
        new("StreamRoleBlacklistedUser", Owner: new("StreamRoleSettingsId", "StreamRoleSettings"), HasGuildId: false),
        new("StreamRoleWhitelistedUser", Owner: new("StreamRoleSettingsId", "StreamRoleSettings"), HasGuildId: false),
        new("AntiSpamSetting"),
        new("AntiSpamIgnore", Owner: new("AntiSpamSettingId", "AntiSpamSetting"), HasGuildId: false),
        new("AutoTranslateChannels"),
        new("AutoTranslateUsers", Owner: new("ChannelId", "AutoTranslateChannels"), HasGuildId: false),
        new("GuildFilterConfig"),
        new("FilterChannelId", Owner: new("GuildFilterConfigId", "GuildFilterConfig"), HasGuildId: false),
        new("FilterLinksChannelId", Owner: new("GuildFilterConfigId", "GuildFilterConfig"), HasGuildId: false),
        new("FilterWordsChannelId", Owner: new("GuildFilterConfigId", "GuildFilterConfig"), HasGuildId: false),
        new("FilteredWord", Owner: new("GuildFilterConfigId", "GuildFilterConfig"), HasGuildId: false),
        new("SarGroup"),
        new("Sar", Owner: new("SarGroupId", "SarGroup")),
        new("ShopEntry"),
        new("ShopEntryItem", Owner: new("ShopEntryId", "ShopEntry"), HasGuildId: false),
        new("XpSettings"),
        new("XpCurrencyReward", Owner: new("XpSettingsId", "XpSettings"), HasGuildId: false),
        new("XpRoleReward", Owner: new("XpSettingsId", "XpSettings"), HasGuildId: false),
        new("AiAgentGuildSkill"),
        new("AntiAltSetting"),
        new("AntiRaidSetting"),
        new("AutoPublishChannel"),
        new("AutoThreadChannel"),
        new("BanTemplates"),
        new("ChannelXpConfig"),
        new("CommandAlias"),
        new("CommandCooldown"),
        new("DelMsgOnCmdChannel"),
        new("DiscordPermOverrides"),
        new("Expressions"),
        new("FeedSub"),
        new("FlagTranslateChannel"),
        new("FollowedStream"),
        new("GCChannelId"),
        new("GreetSettings"),
        new("GuildColors"),
        new("GuildXpConfig"),
        new("HoneyPotChannels", HasIdentity: false),
        new("ImageOnlyChannels"),
        new("LineUpUser", HasIdentity: false),
        new("LinkFix"),
        new("LiveChannelConfig"),
        new("LogChannels"),
        new("LogIgnores"),
        new("MusicPlayerSettings"),
        new("MutedUserId"),
        new("Notify"),
        new("Quotes"),
        new("ReactionRoles"),
        // Private reminders go to a member's DMs, and timely reminders are about the old bot's currency.
        new("Reminders", ExtraWhere: "\"IsPrivate\" = 0 AND \"Type\" = 0"),
        // The last message is a message of the old bot.
        new("Repeaters", NullOnExport: ["LastMessageId"]),
        new("SarAutoDelete"),
        new("ScheduledCommand"),
        new("SlowmodeIgnoredRole"),
        new("SlowmodeIgnoredUser"),
        new("StarboardConfig"),
        new("StarboardEntry"),
        new("StarboardIgnoredChannel"),
        new("StickyRoles"),
        new("TempRole"),
        new("TempVoiceHub"),
        new("UnbanTimer"),
        new("UnmuteTimer"),
        new("UserRole", HasIdentity: false),
        new(XP_TABLE, IsXp: true),
        new("VcRoleInfo"),
        new("WarnTemplates"),
        new("WarningPunishment"),
        new("Warnings"),
        new("XpExcludedItem"),
    ];

    // Tables with server rows which the export leaves out.
    public static readonly FrozenSet<string> Excluded = FrozenSet.ToFrozenSet(
    [
        // Commands of the bot owner, which run with owner rights.
        "AutoCommands",
        // Buttons, giveaways, starboard posts and temp channels are messages and channels of the old bot.
        "ButtonRole",
        "GiveawayModel",
        "StarboardMessage",
        "TempVoiceChannel",
        // Currency of the old bot.
        "PlantedCurrency",
    ]);

    public static readonly FrozenSet<string> Parents = GetParentsInternal();

    public static readonly FrozenDictionary<string, ServerDataTable> ByName
        = All.ToFrozenDictionary(static t => t.Name);

    // Rows of the guild in the table, as a WHERE clause on the given schema. $g is the guild id.
    // A child table is filtered through the parent rows in parentSchema.
    public static string Where(ServerDataTable table, string parentSchema, string? alias = null)
    {
        var prefix = alias is null ? string.Empty : alias + ".";
        var parts = new List<string>(3);

        if (table.HasGuildId)
            parts.Add($"{prefix}\"{GUILD_COLUMN}\" = $g");

        if (table.Owner is { } owner)
        {
            var parentFilter = ByName[owner.Parent].HasGuildId ? $" WHERE \"{GUILD_COLUMN}\" = $g" : string.Empty;
            parts.Add($"{prefix}\"{owner.Column}\" IN (SELECT \"{ID_COLUMN}\" FROM {parentSchema}.\"{owner.Parent}\"{parentFilter})");
        }

        if (table.ExtraWhere is { } extra)
            parts.Add(extra);

        return string.Join(" AND ", parts);
    }

    private static FrozenSet<string> GetParentsInternal()
    {
        var parents = new HashSet<string>();
        foreach (var table in All)
        {
            if (table.Owner is { } owner)
                parents.Add(owner.Parent);

            if (table.Refs is { } refs)
            {
                foreach (var r in refs)
                    parents.Add(r.Parent);
            }
        }

        return parents.ToFrozenSet();
    }
}
