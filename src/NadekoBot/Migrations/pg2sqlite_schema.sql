CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "AiAgentGuildSkill" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AiAgentGuildSkill" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    "Instruction" TEXT NOT NULL,
    "IsEnabled" INTEGER NOT NULL
);

CREATE TABLE "AntiAltSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiAltSetting" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "MinAge" TEXT NOT NULL,
    "Action" INTEGER NOT NULL,
    "ActionDurationMinutes" INTEGER NOT NULL,
    "RoleId" INTEGER NULL
);

CREATE TABLE "AntiRaidSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiRaidSetting" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "UserThreshold" INTEGER NOT NULL,
    "Seconds" INTEGER NOT NULL,
    "Action" INTEGER NOT NULL,
    "PunishDuration" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "AntiSpamSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiSpamSetting" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Action" INTEGER NOT NULL,
    "MessageThreshold" INTEGER NOT NULL,
    "MuteTime" INTEGER NOT NULL,
    "RoleId" INTEGER NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "AutoCommands" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AutoCommands" PRIMARY KEY AUTOINCREMENT,
    "CommandText" TEXT NULL,
    "ChannelId" INTEGER NOT NULL,
    "ChannelName" TEXT NULL,
    "GuildId" INTEGER NULL,
    "GuildName" TEXT NULL,
    "VoiceChannelId" INTEGER NULL,
    "VoiceChannelName" TEXT NULL,
    "Interval" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "AutoPublishChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AutoPublishChannel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "AutoTranslateChannels" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AutoTranslateChannels" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "AutoDelete" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "BankUsers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_BankUsers" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Balance" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "BanTemplates" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_BanTemplates" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Text" TEXT NULL,
    "PruneDays" INTEGER NULL,
    "DisableUnban" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "Blacklist" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Blacklist" PRIMARY KEY AUTOINCREMENT,
    "ItemId" INTEGER NOT NULL,
    "Type" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "ButtonRole" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ButtonRole" PRIMARY KEY AUTOINCREMENT,
    "ButtonId" TEXT NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL,
    "Position" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "Emote" TEXT NOT NULL,
    "Label" TEXT NOT NULL,
    "Exclusive" INTEGER NOT NULL,
    CONSTRAINT "AK_ButtonRole_RoleId_MessageId" UNIQUE ("RoleId", "MessageId")
);

CREATE TABLE "ChannelSpotOverride" (
    "ChannelId" INTEGER NOT NULL CONSTRAINT "PK_ChannelSpotOverride" PRIMARY KEY AUTOINCREMENT,
    "Spot" INTEGER NOT NULL
);

CREATE TABLE "ChannelXpConfig" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ChannelXpConfig" PRIMARY KEY AUTOINCREMENT,
    "RateType" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "XpAmount" INTEGER NOT NULL,
    "Cooldown" REAL NOT NULL,
    CONSTRAINT "AK_ChannelXpConfig_GuildId_ChannelId_RateType" UNIQUE ("GuildId", "ChannelId", "RateType")
);

CREATE TABLE "CommandAlias" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CommandAlias" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Trigger" TEXT NULL,
    "Mapping" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "CommandCooldown" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CommandCooldown" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Seconds" INTEGER NOT NULL,
    "CommandName" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "CurrencyTransactions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CurrencyTransactions" PRIMARY KEY AUTOINCREMENT,
    "Amount" INTEGER NOT NULL,
    "Note" TEXT NULL,
    "UserId" INTEGER NOT NULL,
    "Type" TEXT NOT NULL,
    "Extra" TEXT NOT NULL,
    "OtherId" INTEGER NULL DEFAULT (NULL),
    "DateAdded" TEXT NULL
);

CREATE TABLE "DelMsgOnCmdChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DelMsgOnCmdChannel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "State" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "DiscordPermOverrides" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DiscordPermOverrides" PRIMARY KEY AUTOINCREMENT,
    "Perm" INTEGER NOT NULL,
    "GuildId" INTEGER NULL,
    "Command" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "Expressions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Expressions" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NULL,
    "Response" TEXT NULL,
    "Trigger" TEXT NULL,
    "AutoDeleteTrigger" INTEGER NOT NULL,
    "DmResponse" INTEGER NOT NULL,
    "ContainsAnywhere" INTEGER NOT NULL,
    "AllowTarget" INTEGER NOT NULL,
    "Reactions" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "FeedSub" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FeedSub" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Url" TEXT NULL,
    "Message" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "FishCatch" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FishCatch" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "FishId" INTEGER NOT NULL,
    "Count" INTEGER NOT NULL,
    "MaxStars" INTEGER NOT NULL,
    CONSTRAINT "AK_FishCatch_UserId_FishId" UNIQUE ("UserId", "FishId")
);

CREATE TABLE "FlagTranslateChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FlagTranslateChannel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "FollowedStream" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FollowedStream" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Username" TEXT NULL,
    "PrettyName" TEXT NULL,
    "Type" INTEGER NOT NULL,
    "Message" TEXT NULL
);

CREATE TABLE "GamblingStats" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GamblingStats" PRIMARY KEY AUTOINCREMENT,
    "Feature" TEXT NULL,
    "Bet" TEXT NOT NULL,
    "PaidOut" TEXT NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "GCChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GCChannelId" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "GiveawayModel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GiveawayModel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Message" TEXT NULL,
    "EndsAt" TEXT NOT NULL
);

CREATE TABLE "GreetSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GreetSettings" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "GreetType" INTEGER NOT NULL,
    "MessageText" TEXT NULL,
    "IsEnabled" INTEGER NOT NULL DEFAULT 0,
    "ChannelId" INTEGER NULL,
    "AutoDeleteTimer" INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE "GuildColors" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GuildColors" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "OkColor" TEXT NULL,
    "ErrorColor" TEXT NULL,
    "PendingColor" TEXT NULL
);

CREATE TABLE "GuildConfigs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GuildConfigs" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Prefix" TEXT NULL,
    "DeleteMessageOnCommand" INTEGER NOT NULL,
    "AutoAssignRoleIds" TEXT NULL,
    "VerbosePermissions" INTEGER NOT NULL,
    "PermissionRole" TEXT NULL,
    "MuteRoleName" TEXT NULL,
    "CleverbotEnabled" INTEGER NOT NULL,
    "WarningsInitialized" INTEGER NOT NULL,
    "GameVoiceChannel" INTEGER NULL,
    "VerboseErrors" INTEGER NOT NULL DEFAULT 1,
    "NotifyStreamOffline" INTEGER NOT NULL,
    "DeleteStreamOnlineMessage" INTEGER NOT NULL,
    "WarnExpireHours" INTEGER NOT NULL,
    "WarnExpireAction" INTEGER NOT NULL,
    "DisableGlobalExpressions" INTEGER NOT NULL,
    "ExpressionOverrideEnabled" INTEGER NOT NULL,
    "StickyRoles" INTEGER NOT NULL,
    "TimeZoneId" TEXT NULL,
    "Locale" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "GuildFilterConfig" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GuildFilterConfig" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "FilterInvites" INTEGER NOT NULL,
    "FilterLinks" INTEGER NOT NULL,
    "FilterWords" INTEGER NOT NULL
);

CREATE TABLE "GuildXpConfig" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GuildXpConfig" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "RateType" INTEGER NOT NULL,
    "XpAmount" INTEGER NOT NULL,
    "Cooldown" REAL NOT NULL,
    "XpTemplateUrl" TEXT NULL,
    CONSTRAINT "AK_GuildXpConfig_GuildId_RateType" UNIQUE ("GuildId", "RateType")
);

CREATE TABLE "HoneyPotChannels" (
    "GuildId" INTEGER NOT NULL CONSTRAINT "PK_HoneyPotChannels" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "Action" INTEGER NOT NULL
);

CREATE TABLE "ImageOnlyChannels" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ImageOnlyChannels" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Type" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "LineUpUser" (
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Reason" TEXT NULL,
    "DateAdded" TEXT NOT NULL,
    CONSTRAINT "PK_LineUpUser" PRIMARY KEY ("GuildId", "ChannelId", "UserId")
);

CREATE TABLE "LinkFix" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LinkFix" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "OldDomain" TEXT NOT NULL,
    "NewDomain" TEXT NOT NULL
);

CREATE TABLE "LiveChannelConfig" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LiveChannelConfig" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Template" TEXT NOT NULL
);

CREATE TABLE "LogChannels" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LogChannels" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "LogType" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL
);

CREATE TABLE "LogIgnores" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LogIgnores" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "LogItemId" INTEGER NOT NULL,
    "ItemType" INTEGER NOT NULL
);

CREATE TABLE "MusicPlayerSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MusicPlayerSettings" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "PlayerRepeat" INTEGER NOT NULL,
    "MusicChannelId" INTEGER NULL,
    "Volume" INTEGER NOT NULL DEFAULT 100,
    "AutoDisconnect" INTEGER NOT NULL,
    "QualityPreset" INTEGER NOT NULL,
    "AutoPlay" INTEGER NOT NULL
);

CREATE TABLE "MusicPlaylists" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MusicPlaylists" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NULL,
    "Author" TEXT NULL,
    "AuthorId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "MutedUserId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MutedUserId" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "NCPixel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_NCPixel" PRIMARY KEY AUTOINCREMENT,
    "Position" INTEGER NOT NULL,
    "Price" INTEGER NOT NULL,
    "OwnerId" INTEGER NOT NULL,
    "Color" INTEGER NOT NULL,
    "Text" TEXT NOT NULL,
    CONSTRAINT "AK_NCPixel_Position" UNIQUE ("Position")
);

CREATE TABLE "Notify" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Notify" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NULL,
    "Type" INTEGER NOT NULL,
    "Message" TEXT NOT NULL,
    CONSTRAINT "AK_Notify_GuildId_Type" UNIQUE ("GuildId", "Type")
);

CREATE TABLE "Patrons" (
    "UserId" INTEGER NOT NULL CONSTRAINT "PK_Patrons" PRIMARY KEY AUTOINCREMENT,
    "UniquePlatformUserId" TEXT NULL,
    "AmountCents" INTEGER NOT NULL,
    "LastCharge" TEXT NOT NULL,
    "ValidThru" TEXT NOT NULL
);

CREATE TABLE "PlantedCurrency" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PlantedCurrency" PRIMARY KEY AUTOINCREMENT,
    "Amount" INTEGER NOT NULL,
    "Password" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "Quotes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Quotes" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Keyword" TEXT NOT NULL,
    "AuthorName" TEXT NOT NULL,
    "AuthorId" INTEGER NOT NULL,
    "Text" TEXT NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "Rakeback" (
    "UserId" INTEGER NOT NULL CONSTRAINT "PK_Rakeback" PRIMARY KEY AUTOINCREMENT,
    "Amount" TEXT NOT NULL
);

CREATE TABLE "ReactionRoles" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ReactionRoles" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL,
    "Emote" TEXT NULL,
    "RoleId" INTEGER NOT NULL,
    "Group" INTEGER NOT NULL,
    "LevelReq" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "Reminders" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Reminders" PRIMARY KEY AUTOINCREMENT,
    "When" TEXT NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "ServerId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Message" TEXT NULL,
    "IsPrivate" INTEGER NOT NULL,
    "Type" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "Repeaters" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Repeaters" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "LastMessageId" INTEGER NULL,
    "Message" TEXT NULL,
    "Interval" TEXT NOT NULL,
    "StartTimeOfDay" TEXT NULL,
    "NoRedundant" INTEGER NOT NULL,
    "DateAdded" TEXT NOT NULL
);

CREATE TABLE "RewardedUsers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_RewardedUsers" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "PlatformUserId" TEXT NULL,
    "AmountRewardedThisMonth" INTEGER NOT NULL,
    "LastReward" TEXT NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "RotatingStatus" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_RotatingStatus" PRIMARY KEY AUTOINCREMENT,
    "Status" TEXT NULL,
    "Type" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "SarAutoDelete" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SarAutoDelete" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "IsEnabled" INTEGER NOT NULL
);

CREATE TABLE "SarGroup" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SarGroup" PRIMARY KEY AUTOINCREMENT,
    "GroupNumber" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "RoleReq" INTEGER NULL,
    "IsExclusive" INTEGER NOT NULL,
    "Name" TEXT NULL,
    CONSTRAINT "AK_SarGroup_GuildId_GroupNumber" UNIQUE ("GuildId", "GroupNumber")
);

CREATE TABLE "ScheduledCommand" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ScheduledCommand" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL,
    "Text" TEXT NOT NULL,
    "When" TEXT NOT NULL
);

CREATE TABLE "ShopEntry" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ShopEntry" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Index" INTEGER NOT NULL,
    "Price" INTEGER NOT NULL,
    "Name" TEXT NULL,
    "AuthorId" INTEGER NOT NULL,
    "Type" INTEGER NOT NULL,
    "RoleName" TEXT NULL,
    "RoleId" INTEGER NOT NULL,
    "RoleRequirement" INTEGER NULL,
    "Command" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "SlowmodeIgnoredRole" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SlowmodeIgnoredRole" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "SlowmodeIgnoredUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SlowmodeIgnoredUser" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "StickyRoles" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StickyRoles" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "RoleIds" TEXT NULL,
    "UserId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "StreamOnlineMessages" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StreamOnlineMessages" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL,
    "Type" INTEGER NOT NULL,
    "Name" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "StreamRoleSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StreamRoleSettings" PRIMARY KEY AUTOINCREMENT,
    "GuildConfigId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "Enabled" INTEGER NOT NULL,
    "AddRoleId" INTEGER NOT NULL,
    "FromRoleId" INTEGER NOT NULL,
    "Keyword" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "TempRole" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TempRole" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Remove" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    CONSTRAINT "AK_TempRole_GuildId_UserId_RoleId" UNIQUE ("GuildId", "UserId", "RoleId")
);

CREATE TABLE "TodosArchive" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TodosArchive" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Name" TEXT NULL
);

CREATE TABLE "UnbanTimer" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UnbanTimer" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "UnbanAt" TEXT NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "UnmuteTimer" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UnmuteTimer" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "UnmuteAt" TEXT NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "UnroleTimer" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UnroleTimer" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "UnbanAt" TEXT NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "UserBetStats" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserBetStats" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Game" INTEGER NOT NULL,
    "WinCount" INTEGER NOT NULL,
    "LoseCount" INTEGER NOT NULL,
    "TotalBet" TEXT NOT NULL,
    "PaidOut" TEXT NOT NULL,
    "MaxWin" INTEGER NOT NULL,
    "MaxBet" INTEGER NOT NULL
);

CREATE TABLE "UserFishItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserFishItem" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "ItemType" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    "IsEquipped" INTEGER NOT NULL,
    "UsesLeft" INTEGER NULL,
    "ExpiresAt" TEXT NULL
);

CREATE TABLE "UserFishStats" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserFishStats" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Skill" INTEGER NOT NULL
);

CREATE TABLE "UserQuest" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserQuest" PRIMARY KEY AUTOINCREMENT,
    "QuestNumber" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "QuestId" INTEGER NOT NULL,
    "Progress" INTEGER NOT NULL,
    "IsCompleted" INTEGER NOT NULL,
    "DateAssigned" TEXT NOT NULL
);

CREATE TABLE "UserRole" (
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    CONSTRAINT "PK_UserRole" PRIMARY KEY ("GuildId", "UserId", "RoleId")
);

CREATE TABLE "UserXpStats" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserXpStats" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "Xp" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "VcRoleInfo" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_VcRoleInfo" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "VoiceChannelId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "WaifuCycle" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuCycle" PRIMARY KEY AUTOINCREMENT,
    "WaifuUserId" INTEGER NOT NULL,
    "CycleNumber" INTEGER NOT NULL,
    "ManagerUserId" INTEGER NOT NULL,
    "WaifuFeePercent" INTEGER NOT NULL,
    "ReturnsCap" INTEGER NOT NULL,
    "ManagerCutPercent" REAL NOT NULL,
    "Price" INTEGER NOT NULL,
    "TotalBacked" INTEGER NOT NULL,
    "Processed" INTEGER NOT NULL,
    "ProcessedAt" TEXT NULL
);

CREATE TABLE "WaifuCycleSnapshot" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuCycleSnapshot" PRIMARY KEY AUTOINCREMENT,
    "CycleNumber" INTEGER NOT NULL,
    "WaifuUserId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "SnapshotBalance" INTEGER NOT NULL
);

CREATE TABLE "WaifuFan" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuFan" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "WaifuUserId" INTEGER NOT NULL,
    "DelegatedAt" TEXT NOT NULL
);

CREATE TABLE "WaifuGiftCount" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuGiftCount" PRIMARY KEY AUTOINCREMENT,
    "WaifuUserId" INTEGER NOT NULL,
    "GiftItemId" TEXT NOT NULL,
    "Count" INTEGER NOT NULL
);

CREATE TABLE "WaifuInfo" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuInfo" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Mood" INTEGER NOT NULL,
    "Food" INTEGER NOT NULL,
    "LastDecayTime" TEXT NOT NULL,
    "WaifuFeePercent" INTEGER NOT NULL,
    "Price" INTEGER NOT NULL,
    "ManagerUserId" INTEGER NULL,
    "TotalProduced" INTEGER NOT NULL,
    "ReturnsCap" INTEGER NOT NULL,
    "IsHubby" INTEGER NOT NULL,
    "CustomAvatarUrl" TEXT NULL,
    "Description" TEXT NULL,
    "Quote" TEXT NULL
);

CREATE TABLE "WaifuPendingPayout" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuPendingPayout" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Amount" TEXT NOT NULL
);

CREATE TABLE "WarningPunishment" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WarningPunishment" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Count" INTEGER NOT NULL,
    "Punishment" INTEGER NOT NULL,
    "Time" INTEGER NOT NULL,
    "RoleId" INTEGER NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "AK_WarningPunishment_GuildId_Count" UNIQUE ("GuildId", "Count")
);

CREATE TABLE "Warnings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Warnings" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Reason" TEXT NULL,
    "Forgiven" INTEGER NOT NULL,
    "ForgivenBy" TEXT NULL,
    "Moderator" TEXT NULL,
    "Weight" INTEGER NOT NULL DEFAULT 1,
    "DateAdded" TEXT NULL
);

CREATE TABLE "WarnTemplates" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WarnTemplates" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Text" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "XpExcludedItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpExcludedItem" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ItemType" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    CONSTRAINT "AK_XpExcludedItem_GuildId_ItemType_ItemId" UNIQUE ("GuildId", "ItemType", "ItemId")
);

CREATE TABLE "XpSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpSettings" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "XpFormulaA" INTEGER NOT NULL,
    "XpFormulaC" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "XpShopOwnedItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpShopOwnedItem" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "ItemType" INTEGER NOT NULL,
    "IsUsing" INTEGER NOT NULL,
    "ItemKey" TEXT NOT NULL,
    "DateAdded" TEXT NULL
);

CREATE TABLE "AntiSpamIgnore" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiSpamIgnore" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "AntiSpamSettingId" INTEGER NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_AntiSpamIgnore_AntiSpamSetting_AntiSpamSettingId" FOREIGN KEY ("AntiSpamSettingId") REFERENCES "AntiSpamSetting" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AutoTranslateUsers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AutoTranslateUsers" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Source" TEXT NULL,
    "Target" TEXT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "AK_AutoTranslateUsers_ChannelId_UserId" UNIQUE ("ChannelId", "UserId"),
    CONSTRAINT "FK_AutoTranslateUsers_AutoTranslateChannels_ChannelId" FOREIGN KEY ("ChannelId") REFERENCES "AutoTranslateChannels" ("Id") ON DELETE CASCADE
);

CREATE TABLE "GiveawayUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GiveawayUser" PRIMARY KEY AUTOINCREMENT,
    "GiveawayId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Name" TEXT NULL,
    CONSTRAINT "FK_GiveawayUser_GiveawayModel_GiveawayId" FOREIGN KEY ("GiveawayId") REFERENCES "GiveawayModel" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Permissions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Permissions" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Index" INTEGER NOT NULL,
    "PrimaryTarget" INTEGER NOT NULL,
    "PrimaryTargetId" INTEGER NOT NULL,
    "SecondaryTarget" INTEGER NOT NULL,
    "SecondaryTargetName" TEXT NULL,
    "IsCustomCommand" INTEGER NOT NULL,
    "State" INTEGER NOT NULL,
    "GuildConfigId" INTEGER NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_Permissions_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id")
);

CREATE TABLE "FilterChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilterChannelId" PRIMARY KEY AUTOINCREMENT,
    "GuildFilterConfigId" INTEGER NULL,
    "ChannelId" INTEGER NOT NULL,
    CONSTRAINT "FK_FilterChannelId_GuildFilterConfig_GuildFilterConfigId" FOREIGN KEY ("GuildFilterConfigId") REFERENCES "GuildFilterConfig" ("Id")
);

CREATE TABLE "FilteredWord" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilteredWord" PRIMARY KEY AUTOINCREMENT,
    "GuildFilterConfigId" INTEGER NULL,
    "Word" TEXT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_FilteredWord_GuildFilterConfig_GuildFilterConfigId" FOREIGN KEY ("GuildFilterConfigId") REFERENCES "GuildFilterConfig" ("Id")
);

CREATE TABLE "FilterLinksChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilterLinksChannelId" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "GuildFilterConfigId" INTEGER NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_FilterLinksChannelId_GuildFilterConfig_GuildFilterConfigId" FOREIGN KEY ("GuildFilterConfigId") REFERENCES "GuildFilterConfig" ("Id")
);

CREATE TABLE "FilterWordsChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilterWordsChannelId" PRIMARY KEY AUTOINCREMENT,
    "GuildFilterConfigId" INTEGER NULL,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_FilterWordsChannelId_GuildFilterConfig_GuildFilterConfigId" FOREIGN KEY ("GuildFilterConfigId") REFERENCES "GuildFilterConfig" ("Id")
);

CREATE TABLE "PlaylistSong" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PlaylistSong" PRIMARY KEY AUTOINCREMENT,
    "Provider" TEXT NULL,
    "ProviderType" INTEGER NOT NULL,
    "Title" TEXT NULL,
    "Uri" TEXT NULL,
    "Query" TEXT NULL,
    "MusicPlaylistId" INTEGER NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_PlaylistSong_MusicPlaylists_MusicPlaylistId" FOREIGN KEY ("MusicPlaylistId") REFERENCES "MusicPlaylists" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Sar" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Sar" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "SarGroupId" INTEGER NOT NULL,
    "LevelReq" INTEGER NOT NULL,
    CONSTRAINT "AK_Sar_GuildId_RoleId" UNIQUE ("GuildId", "RoleId"),
    CONSTRAINT "FK_Sar_SarGroup_SarGroupId" FOREIGN KEY ("SarGroupId") REFERENCES "SarGroup" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ShopEntryItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ShopEntryItem" PRIMARY KEY AUTOINCREMENT,
    "Text" TEXT NULL,
    "ShopEntryId" INTEGER NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_ShopEntryItem_ShopEntry_ShopEntryId" FOREIGN KEY ("ShopEntryId") REFERENCES "ShopEntry" ("Id") ON DELETE CASCADE
);

CREATE TABLE "StreamRoleBlacklistedUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StreamRoleBlacklistedUser" PRIMARY KEY AUTOINCREMENT,
    "StreamRoleSettingsId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Username" TEXT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_StreamRoleBlacklistedUser_StreamRoleSettings_StreamRoleSettingsId" FOREIGN KEY ("StreamRoleSettingsId") REFERENCES "StreamRoleSettings" ("Id") ON DELETE CASCADE
);

CREATE TABLE "StreamRoleWhitelistedUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StreamRoleWhitelistedUser" PRIMARY KEY AUTOINCREMENT,
    "StreamRoleSettingsId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Username" TEXT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_StreamRoleWhitelistedUser_StreamRoleSettings_StreamRoleSettingsId" FOREIGN KEY ("StreamRoleSettingsId") REFERENCES "StreamRoleSettings" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Todos" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Todos" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Todo" TEXT NULL,
    "DateAdded" TEXT NOT NULL,
    "IsDone" INTEGER NOT NULL,
    "ArchiveId" INTEGER NULL,
    CONSTRAINT "FK_Todos_TodosArchive_ArchiveId" FOREIGN KEY ("ArchiveId") REFERENCES "TodosArchive" ("Id") ON DELETE CASCADE
);

CREATE TABLE "XpCurrencyReward" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpCurrencyReward" PRIMARY KEY AUTOINCREMENT,
    "XpSettingsId" INTEGER NOT NULL,
    "Level" INTEGER NOT NULL,
    "Amount" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_XpCurrencyReward_XpSettings_XpSettingsId" FOREIGN KEY ("XpSettingsId") REFERENCES "XpSettings" ("Id") ON DELETE CASCADE
);

CREATE TABLE "XpRoleReward" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpRoleReward" PRIMARY KEY AUTOINCREMENT,
    "XpSettingsId" INTEGER NOT NULL,
    "Level" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "Remove" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_XpRoleReward_XpSettings_XpSettingsId" FOREIGN KEY ("XpSettingsId") REFERENCES "XpSettings" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ClubApplicants" (
    "ClubId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    CONSTRAINT "PK_ClubApplicants" PRIMARY KEY ("ClubId", "UserId"),
    CONSTRAINT "FK_ClubApplicants_Clubs_ClubId" FOREIGN KEY ("ClubId") REFERENCES "Clubs" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ClubApplicants_DiscordUser_UserId" FOREIGN KEY ("UserId") REFERENCES "DiscordUser" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ClubBans" (
    "ClubId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    CONSTRAINT "PK_ClubBans" PRIMARY KEY ("ClubId", "UserId"),
    CONSTRAINT "FK_ClubBans_Clubs_ClubId" FOREIGN KEY ("ClubId") REFERENCES "Clubs" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ClubBans_DiscordUser_UserId" FOREIGN KEY ("UserId") REFERENCES "DiscordUser" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Clubs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Clubs" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NULL,
    "Description" TEXT NULL,
    "ImageUrl" TEXT NULL,
    "BannerUrl" TEXT NULL,
    "Xp" INTEGER NOT NULL,
    "OwnerId" INTEGER NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_Clubs_DiscordUser_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "DiscordUser" ("Id") ON DELETE SET NULL
);

CREATE TABLE "DiscordUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DiscordUser" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Username" TEXT NULL,
    "AvatarId" TEXT NULL,
    "ClubId" INTEGER NULL,
    "IsClubAdmin" INTEGER NOT NULL DEFAULT 0,
    "TotalXp" INTEGER NOT NULL DEFAULT 0,
    "CurrencyAmount" INTEGER NOT NULL DEFAULT 0,
    "DateAdded" TEXT NULL,
    CONSTRAINT "AK_DiscordUser_UserId" UNIQUE ("UserId"),
    CONSTRAINT "FK_DiscordUser_Clubs_ClubId" FOREIGN KEY ("ClubId") REFERENCES "Clubs" ("Id")
);

CREATE INDEX "IX_AiAgentGuildSkill_GuildId" ON "AiAgentGuildSkill" ("GuildId");

CREATE UNIQUE INDEX "IX_AiAgentGuildSkill_GuildId_Name" ON "AiAgentGuildSkill" ("GuildId", "Name");

CREATE UNIQUE INDEX "IX_AntiAltSetting_GuildId" ON "AntiAltSetting" ("GuildId");

CREATE UNIQUE INDEX "IX_AntiRaidSetting_GuildId" ON "AntiRaidSetting" ("GuildId");

CREATE INDEX "IX_AntiSpamIgnore_AntiSpamSettingId" ON "AntiSpamIgnore" ("AntiSpamSettingId");

CREATE UNIQUE INDEX "IX_AntiSpamSetting_GuildId" ON "AntiSpamSetting" ("GuildId");

CREATE UNIQUE INDEX "IX_AutoPublishChannel_GuildId" ON "AutoPublishChannel" ("GuildId");

CREATE UNIQUE INDEX "IX_AutoTranslateChannels_ChannelId" ON "AutoTranslateChannels" ("ChannelId");

CREATE INDEX "IX_AutoTranslateChannels_GuildId" ON "AutoTranslateChannels" ("GuildId");

CREATE UNIQUE INDEX "IX_BankUsers_UserId" ON "BankUsers" ("UserId");

CREATE UNIQUE INDEX "IX_BanTemplates_GuildId" ON "BanTemplates" ("GuildId");

CREATE INDEX "IX_ButtonRole_GuildId" ON "ButtonRole" ("GuildId");

CREATE INDEX "IX_ClubApplicants_UserId" ON "ClubApplicants" ("UserId");

CREATE INDEX "IX_ClubBans_UserId" ON "ClubBans" ("UserId");

CREATE UNIQUE INDEX "IX_Clubs_Name" ON "Clubs" ("Name");

CREATE UNIQUE INDEX "IX_Clubs_OwnerId" ON "Clubs" ("OwnerId");

CREATE INDEX "IX_CommandAlias_GuildId" ON "CommandAlias" ("GuildId");

CREATE UNIQUE INDEX "IX_CommandCooldown_GuildId_CommandName" ON "CommandCooldown" ("GuildId", "CommandName");

CREATE INDEX "IX_CurrencyTransactions_UserId" ON "CurrencyTransactions" ("UserId");

CREATE UNIQUE INDEX "IX_DelMsgOnCmdChannel_GuildId_ChannelId" ON "DelMsgOnCmdChannel" ("GuildId", "ChannelId");

CREATE UNIQUE INDEX "IX_DiscordPermOverrides_GuildId_Command" ON "DiscordPermOverrides" ("GuildId", "Command");

CREATE INDEX "IX_DiscordUser_ClubId" ON "DiscordUser" ("ClubId");

CREATE INDEX "IX_DiscordUser_CurrencyAmount" ON "DiscordUser" ("CurrencyAmount");

CREATE INDEX "IX_DiscordUser_TotalXp" ON "DiscordUser" ("TotalXp");

CREATE INDEX "IX_DiscordUser_UserId" ON "DiscordUser" ("UserId");

CREATE INDEX "IX_DiscordUser_Username" ON "DiscordUser" ("Username");

CREATE UNIQUE INDEX "IX_FeedSub_GuildId_Url" ON "FeedSub" ("GuildId", "Url");

CREATE INDEX "IX_FilterChannelId_GuildFilterConfigId" ON "FilterChannelId" ("GuildFilterConfigId");

CREATE INDEX "IX_FilteredWord_GuildFilterConfigId" ON "FilteredWord" ("GuildFilterConfigId");

CREATE INDEX "IX_FilterLinksChannelId_GuildFilterConfigId" ON "FilterLinksChannelId" ("GuildFilterConfigId");

CREATE INDEX "IX_FilterWordsChannelId_GuildFilterConfigId" ON "FilterWordsChannelId" ("GuildFilterConfigId");

CREATE UNIQUE INDEX "IX_FlagTranslateChannel_GuildId_ChannelId" ON "FlagTranslateChannel" ("GuildId", "ChannelId");

CREATE INDEX "IX_FollowedStream_GuildId_Username_Type" ON "FollowedStream" ("GuildId", "Username", "Type");

CREATE UNIQUE INDEX "IX_GamblingStats_Feature" ON "GamblingStats" ("Feature");

CREATE UNIQUE INDEX "IX_GCChannelId_GuildId_ChannelId" ON "GCChannelId" ("GuildId", "ChannelId");

CREATE UNIQUE INDEX "IX_GiveawayUser_GiveawayId_UserId" ON "GiveawayUser" ("GiveawayId", "UserId");

CREATE UNIQUE INDEX "IX_GreetSettings_GuildId_GreetType" ON "GreetSettings" ("GuildId", "GreetType");

CREATE UNIQUE INDEX "IX_GuildColors_GuildId" ON "GuildColors" ("GuildId");

CREATE UNIQUE INDEX "IX_GuildConfigs_GuildId" ON "GuildConfigs" ("GuildId");

CREATE INDEX "IX_GuildConfigs_WarnExpireHours" ON "GuildConfigs" ("WarnExpireHours");

CREATE INDEX "IX_GuildFilterConfig_GuildId" ON "GuildFilterConfig" ("GuildId");

CREATE UNIQUE INDEX "IX_ImageOnlyChannels_ChannelId" ON "ImageOnlyChannels" ("ChannelId");

CREATE INDEX "IX_LineUpUser_GuildId_ChannelId_DateAdded" ON "LineUpUser" ("GuildId", "ChannelId", "DateAdded");

CREATE UNIQUE INDEX "IX_LinkFix_GuildId_OldDomain" ON "LinkFix" ("GuildId", "OldDomain");

CREATE INDEX "IX_LiveChannelConfig_GuildId" ON "LiveChannelConfig" ("GuildId");

CREATE UNIQUE INDEX "IX_LiveChannelConfig_GuildId_ChannelId" ON "LiveChannelConfig" ("GuildId", "ChannelId");

CREATE UNIQUE INDEX "IX_LogChannels_GuildId_LogType" ON "LogChannels" ("GuildId", "LogType");

CREATE UNIQUE INDEX "IX_LogIgnores_GuildId_LogItemId_ItemType" ON "LogIgnores" ("GuildId", "LogItemId", "ItemType");

CREATE UNIQUE INDEX "IX_MusicPlayerSettings_GuildId" ON "MusicPlayerSettings" ("GuildId");

CREATE UNIQUE INDEX "IX_MutedUserId_GuildId_UserId" ON "MutedUserId" ("GuildId", "UserId");

CREATE INDEX "IX_NCPixel_OwnerId" ON "NCPixel" ("OwnerId");

CREATE UNIQUE INDEX "IX_Patrons_UniquePlatformUserId" ON "Patrons" ("UniquePlatformUserId");

CREATE INDEX "IX_Permissions_GuildConfigId" ON "Permissions" ("GuildConfigId");

CREATE INDEX "IX_Permissions_GuildId" ON "Permissions" ("GuildId");

CREATE INDEX "IX_PlantedCurrency_ChannelId" ON "PlantedCurrency" ("ChannelId");

CREATE UNIQUE INDEX "IX_PlantedCurrency_MessageId" ON "PlantedCurrency" ("MessageId");

CREATE INDEX "IX_PlaylistSong_MusicPlaylistId" ON "PlaylistSong" ("MusicPlaylistId");

CREATE INDEX "IX_Quotes_GuildId" ON "Quotes" ("GuildId");

CREATE INDEX "IX_Quotes_Keyword" ON "Quotes" ("Keyword");

CREATE INDEX "IX_ReactionRoles_GuildId" ON "ReactionRoles" ("GuildId");

CREATE UNIQUE INDEX "IX_ReactionRoles_MessageId_Emote" ON "ReactionRoles" ("MessageId", "Emote");

CREATE INDEX "IX_Reminders_When" ON "Reminders" ("When");

CREATE UNIQUE INDEX "IX_RewardedUsers_PlatformUserId" ON "RewardedUsers" ("PlatformUserId");

CREATE INDEX "IX_Sar_SarGroupId" ON "Sar" ("SarGroupId");

CREATE UNIQUE INDEX "IX_SarAutoDelete_GuildId" ON "SarAutoDelete" ("GuildId");

CREATE INDEX "IX_ScheduledCommand_GuildId" ON "ScheduledCommand" ("GuildId");

CREATE INDEX "IX_ScheduledCommand_UserId" ON "ScheduledCommand" ("UserId");

CREATE INDEX "IX_ScheduledCommand_When" ON "ScheduledCommand" ("When");

CREATE UNIQUE INDEX "IX_ShopEntry_GuildId_Index" ON "ShopEntry" ("GuildId", "Index");

CREATE INDEX "IX_ShopEntryItem_ShopEntryId" ON "ShopEntryItem" ("ShopEntryId");

CREATE UNIQUE INDEX "IX_SlowmodeIgnoredRole_GuildId_RoleId" ON "SlowmodeIgnoredRole" ("GuildId", "RoleId");

CREATE UNIQUE INDEX "IX_SlowmodeIgnoredUser_GuildId_UserId" ON "SlowmodeIgnoredUser" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_StickyRoles_GuildId_UserId" ON "StickyRoles" ("GuildId", "UserId");

CREATE INDEX "IX_StreamRoleBlacklistedUser_StreamRoleSettingsId" ON "StreamRoleBlacklistedUser" ("StreamRoleSettingsId");

CREATE UNIQUE INDEX "IX_StreamRoleSettings_GuildId" ON "StreamRoleSettings" ("GuildId");

CREATE INDEX "IX_StreamRoleWhitelistedUser_StreamRoleSettingsId" ON "StreamRoleWhitelistedUser" ("StreamRoleSettingsId");

CREATE INDEX "IX_TempRole_ExpiresAt" ON "TempRole" ("ExpiresAt");

CREATE INDEX "IX_Todos_ArchiveId" ON "Todos" ("ArchiveId");

CREATE INDEX "IX_Todos_UserId" ON "Todos" ("UserId");

CREATE UNIQUE INDEX "IX_UnbanTimer_GuildId_UserId" ON "UnbanTimer" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_UnmuteTimer_GuildId_UserId" ON "UnmuteTimer" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_UnroleTimer_GuildId_UserId" ON "UnroleTimer" ("GuildId", "UserId");

CREATE INDEX "IX_UserBetStats_MaxWin" ON "UserBetStats" ("MaxWin");

CREATE UNIQUE INDEX "IX_UserBetStats_UserId_Game" ON "UserBetStats" ("UserId", "Game");

CREATE INDEX "IX_UserFishItem_UserId" ON "UserFishItem" ("UserId");

CREATE UNIQUE INDEX "IX_UserFishStats_UserId" ON "UserFishStats" ("UserId");

CREATE INDEX "IX_UserQuest_UserId" ON "UserQuest" ("UserId");

CREATE UNIQUE INDEX "IX_UserQuest_UserId_QuestNumber_DateAssigned" ON "UserQuest" ("UserId", "QuestNumber", "DateAssigned");

CREATE INDEX "IX_UserRole_GuildId" ON "UserRole" ("GuildId");

CREATE INDEX "IX_UserRole_GuildId_UserId" ON "UserRole" ("GuildId", "UserId");

CREATE INDEX "IX_UserXpStats_GuildId" ON "UserXpStats" ("GuildId");

CREATE INDEX "IX_UserXpStats_UserId" ON "UserXpStats" ("UserId");

CREATE UNIQUE INDEX "IX_UserXpStats_UserId_GuildId" ON "UserXpStats" ("UserId", "GuildId");

CREATE INDEX "IX_UserXpStats_Xp" ON "UserXpStats" ("Xp");

CREATE UNIQUE INDEX "IX_VcRoleInfo_GuildId_VoiceChannelId" ON "VcRoleInfo" ("GuildId", "VoiceChannelId");

CREATE INDEX "IX_WaifuCycle_CycleNumber_Processed" ON "WaifuCycle" ("CycleNumber", "Processed");

CREATE UNIQUE INDEX "IX_WaifuCycle_WaifuUserId_CycleNumber" ON "WaifuCycle" ("WaifuUserId", "CycleNumber");

CREATE INDEX "IX_WaifuCycleSnapshot_CycleNumber_WaifuUserId" ON "WaifuCycleSnapshot" ("CycleNumber", "WaifuUserId");

CREATE UNIQUE INDEX "IX_WaifuCycleSnapshot_CycleNumber_WaifuUserId_UserId" ON "WaifuCycleSnapshot" ("CycleNumber", "WaifuUserId", "UserId");

CREATE UNIQUE INDEX "IX_WaifuFan_UserId" ON "WaifuFan" ("UserId");

CREATE INDEX "IX_WaifuFan_WaifuUserId" ON "WaifuFan" ("WaifuUserId");

CREATE UNIQUE INDEX "IX_WaifuGiftCount_WaifuUserId_GiftItemId" ON "WaifuGiftCount" ("WaifuUserId", "GiftItemId");

CREATE INDEX "IX_WaifuInfo_ManagerUserId" ON "WaifuInfo" ("ManagerUserId");

CREATE UNIQUE INDEX "IX_WaifuInfo_UserId" ON "WaifuInfo" ("UserId");

CREATE UNIQUE INDEX "IX_WaifuPendingPayout_UserId" ON "WaifuPendingPayout" ("UserId");

CREATE INDEX "IX_Warnings_DateAdded" ON "Warnings" ("DateAdded");

CREATE INDEX "IX_Warnings_GuildId" ON "Warnings" ("GuildId");

CREATE INDEX "IX_Warnings_UserId" ON "Warnings" ("UserId");

CREATE UNIQUE INDEX "IX_WarnTemplates_GuildId" ON "WarnTemplates" ("GuildId");

CREATE UNIQUE INDEX "IX_XpCurrencyReward_Level_XpSettingsId" ON "XpCurrencyReward" ("Level", "XpSettingsId");

CREATE INDEX "IX_XpCurrencyReward_XpSettingsId" ON "XpCurrencyReward" ("XpSettingsId");

CREATE INDEX "IX_XpExcludedItem_GuildId" ON "XpExcludedItem" ("GuildId");

CREATE UNIQUE INDEX "IX_XpRoleReward_XpSettingsId_Level" ON "XpRoleReward" ("XpSettingsId", "Level");

CREATE UNIQUE INDEX "IX_XpSettings_GuildId" ON "XpSettings" ("GuildId");

CREATE UNIQUE INDEX "IX_XpShopOwnedItem_UserId_ItemType_ItemKey" ON "XpShopOwnedItem" ("UserId", "ItemType", "ItemKey");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260407082124_init', '9.0.1');

COMMIT;


