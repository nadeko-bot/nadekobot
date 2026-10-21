PRAGMA foreign_keys=OFF;
BEGIN TRANSACTION;
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);
INSERT INTO __EFMigrationsHistory VALUES('20210621042359_squash','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20210707002343_cleanup','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20210911225622_rero-cascade','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20210912182515_boost-messages','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20210912200106_logsettings-independence','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20210914180026_image-only-channels','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20210921204645_logignore-user-channel','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20211015232708_nsfw-blacklist-tags','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20211121002508_weighted-warnings','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20211213145407_atl-rework','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220102102344_crs-rename-to-expressions-perm-rename','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220110105942_filter-settings-cleanup','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220125044401_curtrs-rework-discorduser-defaults','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220213123633_music-autoplay','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220409170828_clubs-refactor','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220427200557_stondel','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220428051304_bank','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220503234243_new-rero','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220614071359_patronage-system','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220623073903_stondel-db-cache','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220703194348_logwarns','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220725112348_xpitemshop','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220727005807_linkonly-channels','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220808141842_remove-obsolete-xp-columns','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220831142504_banprune','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220913190532_shop-role-req','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20220916191702_autopub','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20221003111019_gambling-stats','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20221021192121_toggle-global-expressions','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20221118195152_log-thread','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20221122204324_feed-text','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20240502233144_v5','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20240518221424_guidlconfig-cleanup','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20240611180456_remove-patron-limits','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20240627033508_honeypot','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20240912124710_greet-settings','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20241018004612_warn-split','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20241028033656_ncanvas','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20241102022949_no-discrim-and-flag-translate','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20241104094222_betstats','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20241105072953_rakeback','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20241115171638_sar-rework','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20241126033626_btnroles_guildcolors','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20241207150041_awardedxp-temprole-notify','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20250113135453_fishes','8.0.8');
INSERT INTO __EFMigrationsHistory VALUES('20250118235223_fish-skill','8.0.8');
CREATE TABLE IF NOT EXISTS "AutoCommands" (
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
CREATE TABLE IF NOT EXISTS "BanTemplates" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_BanTemplates" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Text" TEXT NULL,
    "DateAdded" TEXT NULL
, "PruneDays" INTEGER NULL);
CREATE TABLE IF NOT EXISTS "Blacklist" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Blacklist" PRIMARY KEY AUTOINCREMENT,
    "ItemId" INTEGER NOT NULL,
    "Type" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "CurrencyTransactions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CurrencyTransactions" PRIMARY KEY AUTOINCREMENT,
    "Amount" INTEGER NOT NULL,
    "Note" TEXT NULL,
    "UserId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
, "Extra" TEXT NOT NULL DEFAULT '', "OtherId" INTEGER NULL DEFAULT (NULL), "Type" TEXT NOT NULL DEFAULT '');
CREATE TABLE IF NOT EXISTS "DiscordPermOverrides" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DiscordPermOverrides" PRIMARY KEY AUTOINCREMENT,
    "Perm" INTEGER NOT NULL,
    "GuildId" INTEGER NULL,
    "Command" TEXT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "MusicPlayerSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MusicPlayerSettings" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "PlayerRepeat" INTEGER NOT NULL,
    "MusicChannelId" INTEGER NULL,
    "Volume" INTEGER NOT NULL DEFAULT 100,
    "AutoDisconnect" INTEGER NOT NULL,
    "QualityPreset" INTEGER NOT NULL
, "AutoPlay" INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS "MusicPlaylists" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MusicPlaylists" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NULL,
    "Author" TEXT NULL,
    "AuthorId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "PlantedCurrency" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PlantedCurrency" PRIMARY KEY AUTOINCREMENT,
    "Amount" INTEGER NOT NULL,
    "Password" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "Reminders" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Reminders" PRIMARY KEY AUTOINCREMENT,
    "When" TEXT NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "ServerId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Message" TEXT NULL,
    "IsPrivate" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
, "Type" INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS "Repeaters" (
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
CREATE TABLE IF NOT EXISTS "RewardedUsers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_RewardedUsers" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "PlatformUserId" TEXT NULL,
    "AmountRewardedThisMonth" INTEGER NOT NULL,
    "LastReward" TEXT NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "RotatingStatus" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_RotatingStatus" PRIMARY KEY AUTOINCREMENT,
    "Status" TEXT NULL,
    "Type" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "PlaylistSong" (
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
CREATE TABLE IF NOT EXISTS "AntiAltSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiAltSetting" PRIMARY KEY AUTOINCREMENT,
    "GuildConfigId" INTEGER NOT NULL,
    "MinAge" TEXT NOT NULL,
    "Action" INTEGER NOT NULL,
    "ActionDurationMinutes" INTEGER NOT NULL,
    "RoleId" INTEGER NULL,
    CONSTRAINT "FK_AntiAltSetting_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "FeedSub" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FeedSub" PRIMARY KEY AUTOINCREMENT,
    "GuildConfigId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Url" TEXT NOT NULL,
    "DateAdded" TEXT NULL, "Message" TEXT NULL,
    CONSTRAINT "AK_FeedSub_GuildConfigId_Url" UNIQUE ("GuildConfigId", "Url"),
    CONSTRAINT "FK_FeedSub_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "StreamRoleSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StreamRoleSettings" PRIMARY KEY AUTOINCREMENT,
    "GuildConfigId" INTEGER NOT NULL,
    "Enabled" INTEGER NOT NULL,
    "AddRoleId" INTEGER NOT NULL,
    "FromRoleId" INTEGER NOT NULL,
    "Keyword" TEXT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_StreamRoleSettings_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "XpCurrencyReward" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpCurrencyReward" PRIMARY KEY AUTOINCREMENT,
    "XpSettingsId" INTEGER NOT NULL,
    "Level" INTEGER NOT NULL,
    "Amount" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_XpCurrencyReward_XpSettings_XpSettingsId" FOREIGN KEY ("XpSettingsId") REFERENCES "XpSettings" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "XpRoleReward" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpRoleReward" PRIMARY KEY AUTOINCREMENT,
    "XpSettingsId" INTEGER NOT NULL,
    "Level" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "Remove" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_XpRoleReward_XpSettings_XpSettingsId" FOREIGN KEY ("XpSettingsId") REFERENCES "XpSettings" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "ClubApplicants" (
    "ClubId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    CONSTRAINT "PK_ClubApplicants" PRIMARY KEY ("ClubId", "UserId"),
    CONSTRAINT "FK_ClubApplicants_Clubs_ClubId" FOREIGN KEY ("ClubId") REFERENCES "Clubs" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ClubApplicants_DiscordUser_UserId" FOREIGN KEY ("UserId") REFERENCES "DiscordUser" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "ClubBans" (
    "ClubId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    CONSTRAINT "PK_ClubBans" PRIMARY KEY ("ClubId", "UserId"),
    CONSTRAINT "FK_ClubBans_Clubs_ClubId" FOREIGN KEY ("ClubId") REFERENCES "Clubs" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ClubBans_DiscordUser_UserId" FOREIGN KEY ("UserId") REFERENCES "DiscordUser" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "WaifuInfo" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuInfo" PRIMARY KEY AUTOINCREMENT,
    "WaifuId" INTEGER NOT NULL,
    "ClaimerId" INTEGER NULL,
    "AffinityId" INTEGER NULL,
    "Price" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_WaifuInfo_DiscordUser_AffinityId" FOREIGN KEY ("AffinityId") REFERENCES "DiscordUser" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_WaifuInfo_DiscordUser_ClaimerId" FOREIGN KEY ("ClaimerId") REFERENCES "DiscordUser" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_WaifuInfo_DiscordUser_WaifuId" FOREIGN KEY ("WaifuId") REFERENCES "DiscordUser" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "WaifuUpdates" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuUpdates" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "UpdateType" INTEGER NOT NULL,
    "OldId" INTEGER NULL,
    "NewId" INTEGER NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_WaifuUpdates_DiscordUser_NewId" FOREIGN KEY ("NewId") REFERENCES "DiscordUser" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_WaifuUpdates_DiscordUser_OldId" FOREIGN KEY ("OldId") REFERENCES "DiscordUser" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_WaifuUpdates_DiscordUser_UserId" FOREIGN KEY ("UserId") REFERENCES "DiscordUser" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "LogSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LogSettings" PRIMARY KEY AUTOINCREMENT,
    "ChannelCreatedId" INTEGER NULL,
    "ChannelDestroyedId" INTEGER NULL,
    "ChannelUpdatedId" INTEGER NULL,
    "DateAdded" TEXT NULL,
    "LogOtherId" INTEGER NULL,
    "LogUserPresenceId" INTEGER NULL,
    "LogVoicePresenceId" INTEGER NULL,
    "LogVoicePresenceTTSId" INTEGER NULL,
    "MessageDeletedId" INTEGER NULL,
    "MessageUpdatedId" INTEGER NULL,
    "UserBannedId" INTEGER NULL,
    "UserJoinedId" INTEGER NULL,
    "UserLeftId" INTEGER NULL,
    "UserMutedId" INTEGER NULL,
    "UserUnbannedId" INTEGER NULL,
    "UserUpdatedId" INTEGER NULL
, "GuildId" INTEGER NOT NULL DEFAULT 0, "LogWarnsId" INTEGER NULL, "ThreadCreatedId" INTEGER NULL, "ThreadDeletedId" INTEGER NULL);
CREATE TABLE IF NOT EXISTS "XpSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpSettings" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "ServerExcluded" INTEGER NOT NULL,
    CONSTRAINT "FK_XpSettings_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "WaifuItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuItem" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "ItemEmoji" TEXT NULL,
    "Name" TEXT NULL,
    "WaifuInfoId" INTEGER NULL,
    CONSTRAINT "FK_WaifuItem_WaifuInfo_WaifuInfoId" FOREIGN KEY ("WaifuInfoId") REFERENCES "WaifuInfo" ("Id")
);
CREATE TABLE IF NOT EXISTS "Quotes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Quotes" PRIMARY KEY AUTOINCREMENT,
    "AuthorId" INTEGER NOT NULL,
    "AuthorName" TEXT NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "Keyword" TEXT NOT NULL,
    "Text" TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS "ImageOnlyChannels" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ImageOnlyChannels" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
, "Type" INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS "IgnoredLogChannels" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_IgnoredLogChannels" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "ItemType" INTEGER NOT NULL,
    "LogItemId" INTEGER NOT NULL,
    "LogSettingId" INTEGER NOT NULL,
    CONSTRAINT "FK_IgnoredLogChannels_LogSettings_LogSettingId" FOREIGN KEY ("LogSettingId") REFERENCES "LogSettings" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "AutoTranslateChannels" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AutoTranslateChannels" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "AutoDelete" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "AutoTranslateUsers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AutoTranslateUsers" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Source" TEXT NULL,
    "Target" TEXT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "AK_AutoTranslateUsers_ChannelId_UserId" UNIQUE ("ChannelId", "UserId"),
    CONSTRAINT "FK_AutoTranslateUsers_AutoTranslateChannels_ChannelId" FOREIGN KEY ("ChannelId") REFERENCES "AutoTranslateChannels" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "Expressions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Expressions" PRIMARY KEY AUTOINCREMENT,
    "AllowTarget" INTEGER NOT NULL,
    "AutoDeleteTrigger" INTEGER NOT NULL,
    "ContainsAnywhere" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "DmResponse" INTEGER NOT NULL,
    "GuildId" INTEGER NULL,
    "Reactions" TEXT NULL,
    "Response" TEXT NULL,
    "Trigger" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "Warnings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Warnings" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "Forgiven" INTEGER NOT NULL,
    "ForgivenBy" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "Moderator" TEXT NULL,
    "Reason" TEXT NULL,
    "UserId" INTEGER NOT NULL,
    "Weight" INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE IF NOT EXISTS "BankUsers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_BankUsers" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Balance" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "ReactionRoles" (
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
CREATE TABLE IF NOT EXISTS "Patrons" (
    "UserId" INTEGER NOT NULL CONSTRAINT "PK_Patrons" PRIMARY KEY AUTOINCREMENT,
    "UniquePlatformUserId" TEXT NULL,
    "AmountCents" INTEGER NOT NULL,
    "LastCharge" TEXT NOT NULL,
    "ValidThru" TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS "StreamOnlineMessages" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StreamOnlineMessages" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "MessageId" INTEGER NOT NULL,
    "Name" TEXT NULL,
    "Type" INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS "XpShopOwnedItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpShopOwnedItem" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "ItemType" INTEGER NOT NULL,
    "IsUsing" INTEGER NOT NULL,
    "ItemKey" TEXT NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "AutoPublishChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AutoPublishChannel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "GamblingStats" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GamblingStats" PRIMARY KEY AUTOINCREMENT,
    "Feature" TEXT NULL,
    "Bet" TEXT NOT NULL,
    "PaidOut" TEXT NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "GiveawayModel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GiveawayModel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Message" TEXT NULL,
    "EndsAt" TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS "StickyRoles" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StickyRoles" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "RoleIds" TEXT NULL,
    "UserId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "TodosArchive" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TodosArchive" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Name" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "GiveawayUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GiveawayUser" PRIMARY KEY AUTOINCREMENT,
    "GiveawayId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Name" TEXT NULL,
    CONSTRAINT "FK_GiveawayUser_GiveawayModel_GiveawayId" FOREIGN KEY ("GiveawayId") REFERENCES "GiveawayModel" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "Todos" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Todos" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Todo" TEXT NULL,
    "DateAdded" TEXT NOT NULL,
    "IsDone" INTEGER NOT NULL,
    "ArchiveId" INTEGER NULL,
    CONSTRAINT "FK_Todos_TodosArchive_ArchiveId" FOREIGN KEY ("ArchiveId") REFERENCES "TodosArchive" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "Clubs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Clubs" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "Description" TEXT NULL,
    "ImageUrl" TEXT NULL,
    "Name" TEXT NULL,
    "OwnerId" INTEGER NULL,
    "Xp" INTEGER NOT NULL,
    CONSTRAINT "FK_Clubs_DiscordUser_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "DiscordUser" ("Id") ON DELETE SET NULL
);
CREATE TABLE IF NOT EXISTS "AntiRaidSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiRaidSetting" PRIMARY KEY AUTOINCREMENT,
    "Action" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "PunishDuration" INTEGER NOT NULL,
    "Seconds" INTEGER NOT NULL,
    "UserThreshold" INTEGER NOT NULL,
    CONSTRAINT "FK_AntiRaidSetting_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "AntiSpamIgnore" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiSpamIgnore" PRIMARY KEY AUTOINCREMENT,
    "AntiSpamSettingId" INTEGER NULL,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    CONSTRAINT "FK_AntiSpamIgnore_AntiSpamSetting_AntiSpamSettingId" FOREIGN KEY ("AntiSpamSettingId") REFERENCES "AntiSpamSetting" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "AntiSpamSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiSpamSetting" PRIMARY KEY AUTOINCREMENT,
    "Action" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "MessageThreshold" INTEGER NOT NULL,
    "MuteTime" INTEGER NOT NULL,
    "RoleId" INTEGER NULL,
    CONSTRAINT "FK_AntiSpamSetting_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "CommandAlias" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CommandAlias" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "Mapping" TEXT NULL,
    "Trigger" TEXT NULL,
    CONSTRAINT "FK_CommandAlias_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "CommandCooldown" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CommandCooldown" PRIMARY KEY AUTOINCREMENT,
    "CommandName" TEXT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "Seconds" INTEGER NOT NULL,
    CONSTRAINT "FK_CommandCooldown_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "DelMsgOnCmdChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DelMsgOnCmdChannel" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "State" INTEGER NOT NULL,
    CONSTRAINT "FK_DelMsgOnCmdChannel_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "ExcludedItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ExcludedItem" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "ItemId" INTEGER NOT NULL,
    "ItemType" INTEGER NOT NULL,
    "XpSettingsId" INTEGER NULL,
    CONSTRAINT "FK_ExcludedItem_XpSettings_XpSettingsId" FOREIGN KEY ("XpSettingsId") REFERENCES "XpSettings" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "FilterChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilterChannelId" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    CONSTRAINT "FK_FilterChannelId_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "FilteredWord" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilteredWord" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "Word" TEXT NULL,
    CONSTRAINT "FK_FilteredWord_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "FilterLinksChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilterLinksChannelId" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    CONSTRAINT "FK_FilterLinksChannelId_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "FilterWordsChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilterWordsChannelId" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    CONSTRAINT "FK_FilterWordsChannelId_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "FollowedStream" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FollowedStream" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "GuildId" INTEGER NOT NULL,
    "Message" TEXT NULL,
    "Type" INTEGER NOT NULL,
    "Username" TEXT NULL,
    CONSTRAINT "FK_FollowedStream_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "GCChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GCChannelId" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    CONSTRAINT "FK_GCChannelId_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "MutedUserId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MutedUserId" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "UserId" INTEGER NOT NULL,
    CONSTRAINT "FK_MutedUserId_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "Permissions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Permissions" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "Index" INTEGER NOT NULL,
    "IsCustomCommand" INTEGER NOT NULL,
    "PrimaryTarget" INTEGER NOT NULL,
    "PrimaryTargetId" INTEGER NOT NULL,
    "SecondaryTarget" INTEGER NOT NULL,
    "SecondaryTargetName" TEXT NULL,
    "State" INTEGER NOT NULL,
    CONSTRAINT "FK_Permissions_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "ShopEntry" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ShopEntry" PRIMARY KEY AUTOINCREMENT,
    "AuthorId" INTEGER NOT NULL,
    "Command" TEXT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "Index" INTEGER NOT NULL,
    "Name" TEXT NULL,
    "Price" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "RoleName" TEXT NULL,
    "RoleRequirement" INTEGER NULL,
    "Type" INTEGER NOT NULL,
    CONSTRAINT "FK_ShopEntry_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "ShopEntryItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ShopEntryItem" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "ShopEntryId" INTEGER NULL,
    "Text" TEXT NULL,
    CONSTRAINT "FK_ShopEntryItem_ShopEntry_ShopEntryId" FOREIGN KEY ("ShopEntryId") REFERENCES "ShopEntry" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "SlowmodeIgnoredRole" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SlowmodeIgnoredRole" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "RoleId" INTEGER NOT NULL,
    CONSTRAINT "FK_SlowmodeIgnoredRole_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "SlowmodeIgnoredUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SlowmodeIgnoredUser" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "UserId" INTEGER NOT NULL,
    CONSTRAINT "FK_SlowmodeIgnoredUser_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "StreamRoleBlacklistedUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StreamRoleBlacklistedUser" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "StreamRoleSettingsId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Username" TEXT NULL,
    CONSTRAINT "FK_StreamRoleBlacklistedUser_StreamRoleSettings_StreamRoleSettingsId" FOREIGN KEY ("StreamRoleSettingsId") REFERENCES "StreamRoleSettings" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "StreamRoleWhitelistedUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StreamRoleWhitelistedUser" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "StreamRoleSettingsId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Username" TEXT NULL,
    CONSTRAINT "FK_StreamRoleWhitelistedUser_StreamRoleSettings_StreamRoleSettingsId" FOREIGN KEY ("StreamRoleSettingsId") REFERENCES "StreamRoleSettings" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "UnbanTimer" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UnbanTimer" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "UnbanAt" TEXT NOT NULL,
    "UserId" INTEGER NOT NULL,
    CONSTRAINT "FK_UnbanTimer_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "UnmuteTimer" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UnmuteTimer" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "UnmuteAt" TEXT NOT NULL,
    "UserId" INTEGER NOT NULL,
    CONSTRAINT "FK_UnmuteTimer_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "UnroleTimer" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UnroleTimer" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "RoleId" INTEGER NOT NULL,
    "UnbanAt" TEXT NOT NULL,
    "UserId" INTEGER NOT NULL,
    CONSTRAINT "FK_UnroleTimer_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "VcRoleInfo" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_VcRoleInfo" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "RoleId" INTEGER NOT NULL,
    "VoiceChannelId" INTEGER NOT NULL,
    CONSTRAINT "FK_VcRoleInfo_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "HoneyPotChannels" (
    "GuildId" INTEGER NOT NULL CONSTRAINT "PK_HoneyPotChannels" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS "GreetSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GreetSettings" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "GreetType" INTEGER NOT NULL,
    "MessageText" TEXT NULL,
    "IsEnabled" INTEGER NOT NULL DEFAULT 0,
    "ChannelId" INTEGER NULL,
    "AutoDeleteTimer" INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS "GuildConfigs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GuildConfigs" PRIMARY KEY AUTOINCREMENT,
    "AutoAssignRoleIds" TEXT NULL,
    "AutoDeleteSelfAssignedRoleMessages" INTEGER NOT NULL,
    "CleverbotEnabled" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "DeleteMessageOnCommand" INTEGER NOT NULL,
    "DeleteStreamOnlineMessage" INTEGER NOT NULL,
    "DisableGlobalExpressions" INTEGER NOT NULL,
    "ExclusiveSelfAssignedRoles" INTEGER NOT NULL,
    "FilterInvites" INTEGER NOT NULL,
    "FilterLinks" INTEGER NOT NULL,
    "FilterWords" INTEGER NOT NULL,
    "GameVoiceChannel" INTEGER NULL,
    "GuildId" INTEGER NOT NULL,
    "Locale" TEXT NULL,
    "MuteRoleName" TEXT NULL,
    "NotifyStreamOffline" INTEGER NOT NULL,
    "PermissionRole" TEXT NULL,
    "Prefix" TEXT NULL,
    "StickyRoles" INTEGER NOT NULL,
    "TimeZoneId" TEXT NULL,
    "VerboseErrors" INTEGER NOT NULL DEFAULT 1,
    "VerbosePermissions" INTEGER NOT NULL,
    "WarnExpireAction" INTEGER NOT NULL,
    "WarnExpireHours" INTEGER NOT NULL,
    "WarningsInitialized" INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS "WarningPunishment" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WarningPunishment" PRIMARY KEY AUTOINCREMENT,
    "Count" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "Punishment" INTEGER NOT NULL,
    "RoleId" INTEGER NULL,
    "Time" INTEGER NOT NULL,
    CONSTRAINT "AK_WarningPunishment_GuildId_Count" UNIQUE ("GuildId", "Count")
);
CREATE TABLE IF NOT EXISTS "NCPixel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_NCPixel" PRIMARY KEY AUTOINCREMENT,
    "Position" INTEGER NOT NULL,
    "Price" INTEGER NOT NULL,
    "OwnerId" INTEGER NOT NULL,
    "Color" INTEGER NOT NULL,
    "Text" TEXT NOT NULL,
    CONSTRAINT "AK_NCPixel_Position" UNIQUE ("Position")
);
CREATE TABLE IF NOT EXISTS "FlagTranslateChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FlagTranslateChannel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "DiscordUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DiscordUser" PRIMARY KEY AUTOINCREMENT,
    "AvatarId" TEXT NULL,
    "ClubId" INTEGER NULL,
    "CurrencyAmount" INTEGER NOT NULL DEFAULT 0,
    "DateAdded" TEXT NULL,
    "IsClubAdmin" INTEGER NOT NULL DEFAULT 0,
    "NotifyOnLevelUp" INTEGER NOT NULL DEFAULT 0,
    "TotalXp" INTEGER NOT NULL DEFAULT 0,
    "UserId" INTEGER NOT NULL,
    "Username" TEXT NULL,
    CONSTRAINT "AK_DiscordUser_UserId" UNIQUE ("UserId"),
    CONSTRAINT "FK_DiscordUser_Clubs_ClubId" FOREIGN KEY ("ClubId") REFERENCES "Clubs" ("Id")
);
CREATE TABLE IF NOT EXISTS "UserBetStats" (
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
CREATE TABLE IF NOT EXISTS "Rakeback" (
    "UserId" INTEGER NOT NULL CONSTRAINT "PK_Rakeback" PRIMARY KEY AUTOINCREMENT,
    "Amount" TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS "SarAutoDelete" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SarAutoDelete" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "IsEnabled" INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS "SarGroup" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SarGroup" PRIMARY KEY AUTOINCREMENT,
    "GroupNumber" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "RoleReq" INTEGER NULL,
    "IsExclusive" INTEGER NOT NULL,
    "Name" TEXT NULL,
    CONSTRAINT "AK_SarGroup_GuildId_GroupNumber" UNIQUE ("GuildId", "GroupNumber")
);
CREATE TABLE IF NOT EXISTS "Sar" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Sar" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "SarGroupId" INTEGER NOT NULL,
    "LevelReq" INTEGER NOT NULL,
    CONSTRAINT "AK_Sar_GuildId_RoleId" UNIQUE ("GuildId", "RoleId"),
    CONSTRAINT "FK_Sar_SarGroup_SarGroupId" FOREIGN KEY ("SarGroupId") REFERENCES "SarGroup" ("Id") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "ButtonRole" (
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
CREATE TABLE IF NOT EXISTS "GuildColors" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GuildColors" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "OkColor" TEXT NULL,
    "ErrorColor" TEXT NULL,
    "PendingColor" TEXT NULL
);
CREATE TABLE IF NOT EXISTS "Notify" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Notify" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Type" INTEGER NOT NULL,
    "Message" TEXT NOT NULL,
    CONSTRAINT "AK_Notify_GuildId_Type" UNIQUE ("GuildId", "Type")
);
CREATE TABLE IF NOT EXISTS "TempRole" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TempRole" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Remove" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    CONSTRAINT "AK_TempRole_GuildId_UserId_RoleId" UNIQUE ("GuildId", "UserId", "RoleId")
);
CREATE TABLE IF NOT EXISTS "UserXpStats" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserXpStats" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Xp" INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS "FishCatch" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FishCatch" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "FishId" INTEGER NOT NULL,
    "Count" INTEGER NOT NULL,
    "MaxStars" INTEGER NOT NULL,
    CONSTRAINT "AK_FishCatch_UserId_FishId" UNIQUE ("UserId", "FishId")
);
CREATE TABLE IF NOT EXISTS "UserFishStats" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserFishStats" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Skill" INTEGER NOT NULL
);
DELETE FROM sqlite_sequence;
INSERT INTO sqlite_sequence VALUES('LogSettings',0);
INSERT INTO sqlite_sequence VALUES('XpSettings',0);
INSERT INTO sqlite_sequence VALUES('WaifuItem',0);
INSERT INTO sqlite_sequence VALUES('Quotes',0);
INSERT INTO sqlite_sequence VALUES('IgnoredLogChannels',0);
INSERT INTO sqlite_sequence VALUES('Expressions',0);
INSERT INTO sqlite_sequence VALUES('Warnings',0);
INSERT INTO sqlite_sequence VALUES('ReactionRoles',0);
INSERT INTO sqlite_sequence VALUES('StreamOnlineMessages',0);
INSERT INTO sqlite_sequence VALUES('Clubs',0);
INSERT INTO sqlite_sequence VALUES('AntiRaidSetting',0);
INSERT INTO sqlite_sequence VALUES('AntiSpamIgnore',0);
INSERT INTO sqlite_sequence VALUES('AntiSpamSetting',0);
INSERT INTO sqlite_sequence VALUES('CommandAlias',0);
INSERT INTO sqlite_sequence VALUES('CommandCooldown',0);
INSERT INTO sqlite_sequence VALUES('DelMsgOnCmdChannel',0);
INSERT INTO sqlite_sequence VALUES('ExcludedItem',0);
INSERT INTO sqlite_sequence VALUES('FilterChannelId',0);
INSERT INTO sqlite_sequence VALUES('FilteredWord',0);
INSERT INTO sqlite_sequence VALUES('FilterLinksChannelId',0);
INSERT INTO sqlite_sequence VALUES('FilterWordsChannelId',0);
INSERT INTO sqlite_sequence VALUES('FollowedStream',0);
INSERT INTO sqlite_sequence VALUES('GCChannelId',0);
INSERT INTO sqlite_sequence VALUES('MutedUserId',0);
INSERT INTO sqlite_sequence VALUES('Permissions',0);
INSERT INTO sqlite_sequence VALUES('ShopEntry',0);
INSERT INTO sqlite_sequence VALUES('ShopEntryItem',0);
INSERT INTO sqlite_sequence VALUES('SlowmodeIgnoredRole',0);
INSERT INTO sqlite_sequence VALUES('SlowmodeIgnoredUser',0);
INSERT INTO sqlite_sequence VALUES('StreamRoleBlacklistedUser',0);
INSERT INTO sqlite_sequence VALUES('StreamRoleWhitelistedUser',0);
INSERT INTO sqlite_sequence VALUES('UnbanTimer',0);
INSERT INTO sqlite_sequence VALUES('UnmuteTimer',0);
INSERT INTO sqlite_sequence VALUES('UnroleTimer',0);
INSERT INTO sqlite_sequence VALUES('VcRoleInfo',0);
INSERT INTO sqlite_sequence VALUES('GreetSettings',0);
INSERT INTO sqlite_sequence VALUES('GuildConfigs',0);
INSERT INTO sqlite_sequence VALUES('WarningPunishment',0);
INSERT INTO sqlite_sequence VALUES('DiscordUser',0);
INSERT INTO sqlite_sequence VALUES('SarGroup',0);
INSERT INTO sqlite_sequence VALUES('Sar',0);
INSERT INTO sqlite_sequence VALUES('SarAutoDelete',0);
INSERT INTO sqlite_sequence VALUES('UserXpStats',0);
CREATE UNIQUE INDEX "IX_AntiAltSetting_GuildConfigId" ON "AntiAltSetting" ("GuildConfigId");
CREATE UNIQUE INDEX "IX_BanTemplates_GuildId" ON "BanTemplates" ("GuildId");
CREATE INDEX "IX_ClubApplicants_UserId" ON "ClubApplicants" ("UserId");
CREATE INDEX "IX_ClubBans_UserId" ON "ClubBans" ("UserId");
CREATE INDEX "IX_CurrencyTransactions_UserId" ON "CurrencyTransactions" ("UserId");
CREATE UNIQUE INDEX "IX_DiscordPermOverrides_GuildId_Command" ON "DiscordPermOverrides" ("GuildId", "Command");
CREATE UNIQUE INDEX "IX_MusicPlayerSettings_GuildId" ON "MusicPlayerSettings" ("GuildId");
CREATE INDEX "IX_PlantedCurrency_ChannelId" ON "PlantedCurrency" ("ChannelId");
CREATE UNIQUE INDEX "IX_PlantedCurrency_MessageId" ON "PlantedCurrency" ("MessageId");
CREATE INDEX "IX_PlaylistSong_MusicPlaylistId" ON "PlaylistSong" ("MusicPlaylistId");
CREATE INDEX "IX_Reminders_When" ON "Reminders" ("When");
CREATE UNIQUE INDEX "IX_StreamRoleSettings_GuildConfigId" ON "StreamRoleSettings" ("GuildConfigId");
CREATE INDEX "IX_WaifuInfo_AffinityId" ON "WaifuInfo" ("AffinityId");
CREATE INDEX "IX_WaifuInfo_ClaimerId" ON "WaifuInfo" ("ClaimerId");
CREATE INDEX "IX_WaifuInfo_Price" ON "WaifuInfo" ("Price");
CREATE UNIQUE INDEX "IX_WaifuInfo_WaifuId" ON "WaifuInfo" ("WaifuId");
CREATE INDEX "IX_WaifuUpdates_NewId" ON "WaifuUpdates" ("NewId");
CREATE INDEX "IX_WaifuUpdates_OldId" ON "WaifuUpdates" ("OldId");
CREATE INDEX "IX_WaifuUpdates_UserId" ON "WaifuUpdates" ("UserId");
CREATE INDEX "IX_XpCurrencyReward_XpSettingsId" ON "XpCurrencyReward" ("XpSettingsId");
CREATE UNIQUE INDEX "IX_XpRoleReward_XpSettingsId_Level" ON "XpRoleReward" ("XpSettingsId", "Level");
CREATE UNIQUE INDEX "IX_XpSettings_GuildConfigId" ON "XpSettings" ("GuildConfigId");
CREATE INDEX "IX_WaifuItem_WaifuInfoId" ON "WaifuItem" ("WaifuInfoId");
CREATE INDEX "IX_Quotes_GuildId" ON "Quotes" ("GuildId");
CREATE INDEX "IX_Quotes_Keyword" ON "Quotes" ("Keyword");
CREATE UNIQUE INDEX "IX_LogSettings_GuildId" ON "LogSettings" ("GuildId");
CREATE UNIQUE INDEX "IX_ImageOnlyChannels_ChannelId" ON "ImageOnlyChannels" ("ChannelId");
CREATE UNIQUE INDEX "IX_IgnoredLogChannels_LogSettingId_LogItemId_ItemType" ON "IgnoredLogChannels" ("LogSettingId", "LogItemId", "ItemType");
CREATE UNIQUE INDEX "IX_AutoTranslateChannels_ChannelId" ON "AutoTranslateChannels" ("ChannelId");
CREATE INDEX "IX_AutoTranslateChannels_GuildId" ON "AutoTranslateChannels" ("GuildId");
CREATE INDEX "IX_Warnings_DateAdded" ON "Warnings" ("DateAdded");
CREATE INDEX "IX_Warnings_GuildId" ON "Warnings" ("GuildId");
CREATE INDEX "IX_Warnings_UserId" ON "Warnings" ("UserId");
CREATE UNIQUE INDEX "IX_BankUsers_UserId" ON "BankUsers" ("UserId");
CREATE INDEX "IX_ReactionRoles_GuildId" ON "ReactionRoles" ("GuildId");
CREATE UNIQUE INDEX "IX_ReactionRoles_MessageId_Emote" ON "ReactionRoles" ("MessageId", "Emote");
CREATE UNIQUE INDEX "IX_RewardedUsers_PlatformUserId" ON "RewardedUsers" ("PlatformUserId");
CREATE UNIQUE INDEX "IX_Patrons_UniquePlatformUserId" ON "Patrons" ("UniquePlatformUserId");
CREATE UNIQUE INDEX "IX_XpShopOwnedItem_UserId_ItemType_ItemKey" ON "XpShopOwnedItem" ("UserId", "ItemType", "ItemKey");
CREATE UNIQUE INDEX "IX_AutoPublishChannel_GuildId" ON "AutoPublishChannel" ("GuildId");
CREATE UNIQUE INDEX "IX_GamblingStats_Feature" ON "GamblingStats" ("Feature");
CREATE UNIQUE INDEX "IX_GiveawayUser_GiveawayId_UserId" ON "GiveawayUser" ("GiveawayId", "UserId");
CREATE UNIQUE INDEX "IX_StickyRoles_GuildId_UserId" ON "StickyRoles" ("GuildId", "UserId");
CREATE INDEX "IX_Todos_ArchiveId" ON "Todos" ("ArchiveId");
CREATE INDEX "IX_Todos_UserId" ON "Todos" ("UserId");
CREATE UNIQUE INDEX "IX_Clubs_Name" ON "Clubs" ("Name");
CREATE UNIQUE INDEX "IX_Clubs_OwnerId" ON "Clubs" ("OwnerId");
CREATE UNIQUE INDEX "IX_AntiRaidSetting_GuildConfigId" ON "AntiRaidSetting" ("GuildConfigId");
CREATE INDEX "IX_AntiSpamIgnore_AntiSpamSettingId" ON "AntiSpamIgnore" ("AntiSpamSettingId");
CREATE UNIQUE INDEX "IX_AntiSpamSetting_GuildConfigId" ON "AntiSpamSetting" ("GuildConfigId");
CREATE INDEX "IX_CommandAlias_GuildConfigId" ON "CommandAlias" ("GuildConfigId");
CREATE INDEX "IX_CommandCooldown_GuildConfigId" ON "CommandCooldown" ("GuildConfigId");
CREATE INDEX "IX_DelMsgOnCmdChannel_GuildConfigId" ON "DelMsgOnCmdChannel" ("GuildConfigId");
CREATE INDEX "IX_ExcludedItem_XpSettingsId" ON "ExcludedItem" ("XpSettingsId");
CREATE INDEX "IX_FilterChannelId_GuildConfigId" ON "FilterChannelId" ("GuildConfigId");
CREATE INDEX "IX_FilteredWord_GuildConfigId" ON "FilteredWord" ("GuildConfigId");
CREATE INDEX "IX_FilterLinksChannelId_GuildConfigId" ON "FilterLinksChannelId" ("GuildConfigId");
CREATE INDEX "IX_FilterWordsChannelId_GuildConfigId" ON "FilterWordsChannelId" ("GuildConfigId");
CREATE INDEX "IX_FollowedStream_GuildConfigId" ON "FollowedStream" ("GuildConfigId");
CREATE INDEX "IX_GCChannelId_GuildConfigId" ON "GCChannelId" ("GuildConfigId");
CREATE INDEX "IX_MutedUserId_GuildConfigId" ON "MutedUserId" ("GuildConfigId");
CREATE INDEX "IX_Permissions_GuildConfigId" ON "Permissions" ("GuildConfigId");
CREATE INDEX "IX_ShopEntry_GuildConfigId" ON "ShopEntry" ("GuildConfigId");
CREATE INDEX "IX_ShopEntryItem_ShopEntryId" ON "ShopEntryItem" ("ShopEntryId");
CREATE INDEX "IX_SlowmodeIgnoredRole_GuildConfigId" ON "SlowmodeIgnoredRole" ("GuildConfigId");
CREATE INDEX "IX_SlowmodeIgnoredUser_GuildConfigId" ON "SlowmodeIgnoredUser" ("GuildConfigId");
CREATE INDEX "IX_StreamRoleBlacklistedUser_StreamRoleSettingsId" ON "StreamRoleBlacklistedUser" ("StreamRoleSettingsId");
CREATE INDEX "IX_StreamRoleWhitelistedUser_StreamRoleSettingsId" ON "StreamRoleWhitelistedUser" ("StreamRoleSettingsId");
CREATE INDEX "IX_UnbanTimer_GuildConfigId" ON "UnbanTimer" ("GuildConfigId");
CREATE INDEX "IX_UnmuteTimer_GuildConfigId" ON "UnmuteTimer" ("GuildConfigId");
CREATE INDEX "IX_UnroleTimer_GuildConfigId" ON "UnroleTimer" ("GuildConfigId");
CREATE INDEX "IX_VcRoleInfo_GuildConfigId" ON "VcRoleInfo" ("GuildConfigId");
CREATE UNIQUE INDEX "IX_GreetSettings_GuildId_GreetType" ON "GreetSettings" ("GuildId", "GreetType");
CREATE UNIQUE INDEX "IX_GuildConfigs_GuildId" ON "GuildConfigs" ("GuildId");
CREATE INDEX "IX_GuildConfigs_WarnExpireHours" ON "GuildConfigs" ("WarnExpireHours");
CREATE INDEX "IX_NCPixel_OwnerId" ON "NCPixel" ("OwnerId");
CREATE UNIQUE INDEX "IX_FlagTranslateChannel_GuildId_ChannelId" ON "FlagTranslateChannel" ("GuildId", "ChannelId");
CREATE INDEX "IX_DiscordUser_ClubId" ON "DiscordUser" ("ClubId");
CREATE INDEX "IX_DiscordUser_CurrencyAmount" ON "DiscordUser" ("CurrencyAmount");
CREATE INDEX "IX_DiscordUser_TotalXp" ON "DiscordUser" ("TotalXp");
CREATE INDEX "IX_DiscordUser_UserId" ON "DiscordUser" ("UserId");
CREATE INDEX "IX_DiscordUser_Username" ON "DiscordUser" ("Username");
CREATE UNIQUE INDEX "IX_UserBetStats_UserId_Game" ON "UserBetStats" ("UserId", "Game");
CREATE INDEX "IX_Sar_SarGroupId" ON "Sar" ("SarGroupId");
CREATE UNIQUE INDEX "IX_SarAutoDelete_GuildId" ON "SarAutoDelete" ("GuildId");
CREATE INDEX "IX_ButtonRole_GuildId" ON "ButtonRole" ("GuildId");
CREATE UNIQUE INDEX "IX_GuildColors_GuildId" ON "GuildColors" ("GuildId");
CREATE INDEX "IX_UserBetStats_MaxWin" ON "UserBetStats" ("MaxWin");
CREATE INDEX "IX_TempRole_ExpiresAt" ON "TempRole" ("ExpiresAt");
CREATE INDEX "IX_UserXpStats_GuildId" ON "UserXpStats" ("GuildId");
CREATE INDEX "IX_UserXpStats_UserId" ON "UserXpStats" ("UserId");
CREATE UNIQUE INDEX "IX_UserXpStats_UserId_GuildId" ON "UserXpStats" ("UserId", "GuildId");
CREATE INDEX "IX_UserXpStats_Xp" ON "UserXpStats" ("Xp");
CREATE UNIQUE INDEX "IX_UserFishStats_UserId" ON "UserFishStats" ("UserId");
COMMIT;
