BEGIN TRANSACTION;

DROP INDEX "IX_XpSettings_GuildConfigId";

DROP INDEX "IX_VcRoleInfo_GuildConfigId";

DROP INDEX "IX_UnroleTimer_GuildConfigId";

DROP INDEX "IX_UnmuteTimer_GuildConfigId";

DROP INDEX "IX_UnbanTimer_GuildConfigId";

DROP INDEX "IX_StreamRoleSettings_GuildConfigId";

DROP INDEX "IX_SlowmodeIgnoredUser_GuildConfigId";

DROP INDEX "IX_SlowmodeIgnoredRole_GuildConfigId";

DROP INDEX "IX_ShopEntry_GuildConfigId";

DROP INDEX "IX_MutedUserId_GuildConfigId";

DROP INDEX "IX_GCChannelId_GuildConfigId";

DROP INDEX "IX_FollowedStream_GuildConfigId";

DROP INDEX "IX_DelMsgOnCmdChannel_GuildConfigId";

DROP INDEX "IX_CommandCooldown_GuildConfigId";

DROP INDEX "IX_CommandAlias_GuildConfigId";

DROP INDEX "IX_AntiSpamSetting_GuildConfigId";

DROP INDEX "IX_AntiRaidSetting_GuildConfigId";

DROP INDEX "IX_AntiAltSetting_GuildConfigId";

ALTER TABLE "FilterWordsChannelId" RENAME COLUMN "GuildConfigId" TO "GuildFilterConfigId";

DROP INDEX "IX_FilterWordsChannelId_GuildConfigId";

CREATE INDEX "IX_FilterWordsChannelId_GuildFilterConfigId" ON "FilterWordsChannelId" ("GuildFilterConfigId");

ALTER TABLE "FilterLinksChannelId" RENAME COLUMN "GuildConfigId" TO "GuildFilterConfigId";

DROP INDEX "IX_FilterLinksChannelId_GuildConfigId";

CREATE INDEX "IX_FilterLinksChannelId_GuildFilterConfigId" ON "FilterLinksChannelId" ("GuildFilterConfigId");

ALTER TABLE "FilteredWord" RENAME COLUMN "GuildConfigId" TO "GuildFilterConfigId";

DROP INDEX "IX_FilteredWord_GuildConfigId";

CREATE INDEX "IX_FilteredWord_GuildFilterConfigId" ON "FilteredWord" ("GuildFilterConfigId");

ALTER TABLE "FilterChannelId" RENAME COLUMN "GuildConfigId" TO "GuildFilterConfigId";

DROP INDEX "IX_FilterChannelId_GuildConfigId";

CREATE INDEX "IX_FilterChannelId_GuildFilterConfigId" ON "FilterChannelId" ("GuildFilterConfigId");

ALTER TABLE "XpSettings" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "VcRoleInfo" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "UnroleTimer" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "UnmuteTimer" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "UnbanTimer" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "StreamRoleSettings" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "SlowmodeIgnoredUser" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "SlowmodeIgnoredRole" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "ShopEntry" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Permissions" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "MutedUserId" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "GCChannelId" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "FeedSub" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "DelMsgOnCmdChannel" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "CommandCooldown" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "CommandAlias" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "AntiSpamSetting" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "AntiRaidSetting" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "AntiAltSetting" ADD "GuildId" INTEGER NOT NULL DEFAULT 0;

CREATE TABLE "GuildFilterConfig" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GuildFilterConfig" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "FilterInvites" INTEGER NOT NULL,
    "FilterLinks" INTEGER NOT NULL,
    "FilterWords" INTEGER NOT NULL
);

insert into guildfilterconfig (Id, GuildId, FilterLinks, FilterInvites, FilterWords)
select Id, GuildId, FilterLinks, FilterInvites, FilterWords
from guildconfigs;

DELETE FROM XPSettings WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE XpSettings
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = XpSettings.GuildConfigId);

DELETE FROM StreamRoleSettings WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE StreamRoleSettings
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = StreamRoleSettings.GuildConfigId);

DELETE FROM FeedSub WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE FeedSub
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = FeedSub.GuildConfigId);

DELETE FROM DelMsgOnCmdChannel WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE DelMsgOnCmdChannel
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = DelMsgOnCmdChannel.GuildConfigId);

DELETE FROM AntiSpamSetting WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE AntiSpamSetting
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = AntiSpamSetting.GuildConfigId);

DELETE FROM AntiRaidSetting WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE AntiRaidSetting
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = AntiRaidSetting.GuildConfigId);

DELETE FROM AntiAltSetting WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE AntiAltSetting
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = AntiAltSetting.GuildConfigId);

DELETE FROM VcRoleInfo WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE VcRoleInfo
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = VcRoleInfo.GuildConfigId);

DELETE FROM UnroleTimer WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE UnroleTimer
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = UnroleTimer.GuildConfigId);

DELETE FROM UnmuteTimer WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE UnmuteTimer
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = UnmuteTimer.GuildConfigId);

DELETE FROM UnbanTimer WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE UnbanTimer
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = UnbanTimer.GuildConfigId);

DELETE FROM SlowmodeIgnoredUser WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE SlowmodeIgnoredUser
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = SlowmodeIgnoredUser.GuildConfigId);

DELETE FROM SlowmodeIgnoredRole WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE SlowmodeIgnoredRole
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = SlowmodeIgnoredRole.GuildConfigId);

DELETE FROM ShopEntry WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE ShopEntry
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = ShopEntry.GuildConfigId);

DELETE FROM Permissions WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE Permissions
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = Permissions.GuildConfigId);

DELETE FROM MutedUserId WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE MutedUserId
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = MutedUserId.GuildConfigId);

DELETE FROM GcChannelId WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE GcChannelId
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = GcChannelId.GuildConfigId);

DELETE FROM CommandCooldown WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE CommandCooldown
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = CommandCooldown.GuildConfigId);

DELETE FROM CommandAlias WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE CommandAlias
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = CommandAlias.GuildConfigId);

CREATE UNIQUE INDEX "IX_XpSettings_GuildId" ON "XpSettings" ("GuildId");

CREATE UNIQUE INDEX "IX_XpCurrencyReward_Level_XpSettingsId" ON "XpCurrencyReward" ("Level", "XpSettingsId");

CREATE UNIQUE INDEX "IX_VcRoleInfo_GuildId_VoiceChannelId" ON "VcRoleInfo" ("GuildId", "VoiceChannelId");

CREATE UNIQUE INDEX "IX_UnroleTimer_GuildId_UserId" ON "UnroleTimer" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_UnmuteTimer_GuildId_UserId" ON "UnmuteTimer" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_UnbanTimer_GuildId_UserId" ON "UnbanTimer" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_StreamRoleSettings_GuildId" ON "StreamRoleSettings" ("GuildId");

CREATE UNIQUE INDEX "IX_SlowmodeIgnoredUser_GuildId_UserId" ON "SlowmodeIgnoredUser" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_SlowmodeIgnoredRole_GuildId_RoleId" ON "SlowmodeIgnoredRole" ("GuildId", "RoleId");

CREATE UNIQUE INDEX "IX_ShopEntry_GuildId_Index" ON "ShopEntry" ("GuildId", "Index");

CREATE INDEX "IX_Permissions_GuildId" ON "Permissions" ("GuildId");

CREATE UNIQUE INDEX "IX_MutedUserId_GuildId_UserId" ON "MutedUserId" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_GCChannelId_GuildId_ChannelId" ON "GCChannelId" ("GuildId", "ChannelId");

CREATE INDEX "IX_FollowedStream_GuildId_Username_Type" ON "FollowedStream" ("GuildId", "Username", "Type");

CREATE UNIQUE INDEX "IX_FeedSub_GuildId_Url" ON "FeedSub" ("GuildId", "Url");

CREATE UNIQUE INDEX "IX_DelMsgOnCmdChannel_GuildId_ChannelId" ON "DelMsgOnCmdChannel" ("GuildId", "ChannelId");

CREATE UNIQUE INDEX "IX_CommandCooldown_GuildId_CommandName" ON "CommandCooldown" ("GuildId", "CommandName");

CREATE INDEX "IX_CommandAlias_GuildId" ON "CommandAlias" ("GuildId");

CREATE UNIQUE INDEX "IX_AntiSpamSetting_GuildId" ON "AntiSpamSetting" ("GuildId");

CREATE UNIQUE INDEX "IX_AntiRaidSetting_GuildId" ON "AntiRaidSetting" ("GuildId");

CREATE UNIQUE INDEX "IX_AntiAltSetting_GuildId" ON "AntiAltSetting" ("GuildId");

CREATE INDEX "IX_GuildFilterConfig_GuildId" ON "GuildFilterConfig" ("GuildId");

CREATE TABLE "ef_temp_AntiAltSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiAltSetting" PRIMARY KEY AUTOINCREMENT,
    "Action" INTEGER NOT NULL,
    "ActionDurationMinutes" INTEGER NOT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "MinAge" TEXT NOT NULL,
    "RoleId" INTEGER NULL
);

INSERT INTO "ef_temp_AntiAltSetting" ("Id", "Action", "ActionDurationMinutes", "GuildConfigId", "GuildId", "MinAge", "RoleId")
SELECT "Id", "Action", "ActionDurationMinutes", "GuildConfigId", "GuildId", "MinAge", "RoleId"
FROM "AntiAltSetting";

CREATE TABLE "ef_temp_AntiRaidSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiRaidSetting" PRIMARY KEY AUTOINCREMENT,
    "Action" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "PunishDuration" INTEGER NOT NULL,
    "Seconds" INTEGER NOT NULL,
    "UserThreshold" INTEGER NOT NULL
);

INSERT INTO "ef_temp_AntiRaidSetting" ("Id", "Action", "DateAdded", "GuildConfigId", "GuildId", "PunishDuration", "Seconds", "UserThreshold")
SELECT "Id", "Action", "DateAdded", "GuildConfigId", "GuildId", "PunishDuration", "Seconds", "UserThreshold"
FROM "AntiRaidSetting";

CREATE TABLE "ef_temp_AntiSpamSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiSpamSetting" PRIMARY KEY AUTOINCREMENT,
    "Action" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "MessageThreshold" INTEGER NOT NULL,
    "MuteTime" INTEGER NOT NULL,
    "RoleId" INTEGER NULL
);

INSERT INTO "ef_temp_AntiSpamSetting" ("Id", "Action", "DateAdded", "GuildConfigId", "GuildId", "MessageThreshold", "MuteTime", "RoleId")
SELECT "Id", "Action", "DateAdded", "GuildConfigId", "GuildId", "MessageThreshold", "MuteTime", "RoleId"
FROM "AntiSpamSetting";

CREATE TABLE "ef_temp_CommandAlias" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CommandAlias" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "Mapping" TEXT NULL,
    "Trigger" TEXT NULL
);

INSERT INTO "ef_temp_CommandAlias" ("Id", "DateAdded", "GuildId", "Mapping", "Trigger")
SELECT "Id", "DateAdded", "GuildId", "Mapping", "Trigger"
FROM "CommandAlias";

CREATE TABLE "ef_temp_CommandCooldown" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CommandCooldown" PRIMARY KEY AUTOINCREMENT,
    "CommandName" TEXT NULL,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "Seconds" INTEGER NOT NULL
);

INSERT INTO "ef_temp_CommandCooldown" ("Id", "CommandName", "DateAdded", "GuildId", "Seconds")
SELECT "Id", "CommandName", "DateAdded", "GuildId", "Seconds"
FROM "CommandCooldown";

CREATE TABLE "ef_temp_DelMsgOnCmdChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DelMsgOnCmdChannel" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "State" INTEGER NOT NULL
);

INSERT INTO "ef_temp_DelMsgOnCmdChannel" ("Id", "ChannelId", "DateAdded", "GuildConfigId", "GuildId", "State")
SELECT "Id", "ChannelId", "DateAdded", "GuildConfigId", "GuildId", "State"
FROM "DelMsgOnCmdChannel";

CREATE TABLE "ef_temp_ExcludedItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ExcludedItem" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "ItemId" INTEGER NOT NULL,
    "ItemType" INTEGER NOT NULL,
    "XpSettingsId" INTEGER NULL,
    CONSTRAINT "FK_ExcludedItem_XpSettings_XpSettingsId" FOREIGN KEY ("XpSettingsId") REFERENCES "XpSettings" ("Id")
);

INSERT INTO "ef_temp_ExcludedItem" ("Id", "DateAdded", "ItemId", "ItemType", "XpSettingsId")
SELECT "Id", "DateAdded", "ItemId", "ItemType", "XpSettingsId"
FROM "ExcludedItem";

CREATE TABLE "ef_temp_FeedSub" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FeedSub" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "Message" TEXT NULL,
    "Url" TEXT NULL
);

INSERT INTO "ef_temp_FeedSub" ("Id", "ChannelId", "DateAdded", "GuildConfigId", "GuildId", "Message", "Url")
SELECT "Id", "ChannelId", "DateAdded", "GuildConfigId", "GuildId", "Message", "Url"
FROM "FeedSub";

CREATE TABLE "ef_temp_FilterChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilterChannelId" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "GuildFilterConfigId" INTEGER NULL,
    CONSTRAINT "FK_FilterChannelId_GuildFilterConfig_GuildFilterConfigId" FOREIGN KEY ("GuildFilterConfigId") REFERENCES "GuildFilterConfig" ("Id")
);

INSERT INTO "ef_temp_FilterChannelId" ("Id", "ChannelId", "GuildFilterConfigId")
SELECT "Id", "ChannelId", "GuildFilterConfigId"
FROM "FilterChannelId";

CREATE TABLE "ef_temp_FilteredWord" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilteredWord" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildFilterConfigId" INTEGER NULL,
    "Word" TEXT NULL,
    CONSTRAINT "FK_FilteredWord_GuildFilterConfig_GuildFilterConfigId" FOREIGN KEY ("GuildFilterConfigId") REFERENCES "GuildFilterConfig" ("Id")
);

INSERT INTO "ef_temp_FilteredWord" ("Id", "DateAdded", "GuildFilterConfigId", "Word")
SELECT "Id", "DateAdded", "GuildFilterConfigId", "Word"
FROM "FilteredWord";

CREATE TABLE "ef_temp_FilterLinksChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilterLinksChannelId" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildFilterConfigId" INTEGER NULL,
    CONSTRAINT "FK_FilterLinksChannelId_GuildFilterConfig_GuildFilterConfigId" FOREIGN KEY ("GuildFilterConfigId") REFERENCES "GuildFilterConfig" ("Id")
);

INSERT INTO "ef_temp_FilterLinksChannelId" ("Id", "ChannelId", "DateAdded", "GuildFilterConfigId")
SELECT "Id", "ChannelId", "DateAdded", "GuildFilterConfigId"
FROM "FilterLinksChannelId";

CREATE TABLE "ef_temp_FilterWordsChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FilterWordsChannelId" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildFilterConfigId" INTEGER NULL,
    CONSTRAINT "FK_FilterWordsChannelId_GuildFilterConfig_GuildFilterConfigId" FOREIGN KEY ("GuildFilterConfigId") REFERENCES "GuildFilterConfig" ("Id")
);

INSERT INTO "ef_temp_FilterWordsChannelId" ("Id", "ChannelId", "DateAdded", "GuildFilterConfigId")
SELECT "Id", "ChannelId", "DateAdded", "GuildFilterConfigId"
FROM "FilterWordsChannelId";

CREATE TABLE "ef_temp_FollowedStream" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FollowedStream" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "Message" TEXT NULL,
    "Type" INTEGER NOT NULL,
    "Username" TEXT NULL
);

INSERT INTO "ef_temp_FollowedStream" ("Id", "ChannelId", "GuildId", "Message", "Type", "Username")
SELECT "Id", "ChannelId", "GuildId", "Message", "Type", "Username"
FROM "FollowedStream";

CREATE TABLE "ef_temp_GCChannelId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GCChannelId" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_GCChannelId" ("Id", "ChannelId", "DateAdded", "GuildId")
SELECT "Id", "ChannelId", "DateAdded", "GuildId"
FROM "GCChannelId";

CREATE TABLE "ef_temp_MutedUserId" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MutedUserId" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_MutedUserId" ("Id", "DateAdded", "GuildId", "UserId")
SELECT "Id", "DateAdded", "GuildId", "UserId"
FROM "MutedUserId";

CREATE TABLE "ef_temp_Permissions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Permissions" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NULL,
    "GuildId" INTEGER NOT NULL,
    "Index" INTEGER NOT NULL,
    "IsCustomCommand" INTEGER NOT NULL,
    "PrimaryTarget" INTEGER NOT NULL,
    "PrimaryTargetId" INTEGER NOT NULL,
    "SecondaryTarget" INTEGER NOT NULL,
    "SecondaryTargetName" TEXT NULL,
    "State" INTEGER NOT NULL,
    CONSTRAINT "FK_Permissions_GuildConfigs_GuildConfigId" FOREIGN KEY ("GuildConfigId") REFERENCES "GuildConfigs" ("Id")
);

INSERT INTO "ef_temp_Permissions" ("Id", "DateAdded", "GuildConfigId", "GuildId", "Index", "IsCustomCommand", "PrimaryTarget", "PrimaryTargetId", "SecondaryTarget", "SecondaryTargetName", "State")
SELECT "Id", "DateAdded", "GuildConfigId", "GuildId", "Index", "IsCustomCommand", "PrimaryTarget", "PrimaryTargetId", "SecondaryTarget", "SecondaryTargetName", "State"
FROM "Permissions";

CREATE TABLE "ef_temp_ShopEntry" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ShopEntry" PRIMARY KEY AUTOINCREMENT,
    "AuthorId" INTEGER NOT NULL,
    "Command" TEXT NULL,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "Index" INTEGER NOT NULL,
    "Name" TEXT NULL,
    "Price" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "RoleName" TEXT NULL,
    "RoleRequirement" INTEGER NULL,
    "Type" INTEGER NOT NULL
);

INSERT INTO "ef_temp_ShopEntry" ("Id", "AuthorId", "Command", "DateAdded", "GuildId", "Index", "Name", "Price", "RoleId", "RoleName", "RoleRequirement", "Type")
SELECT "Id", "AuthorId", "Command", "DateAdded", "GuildId", "Index", "Name", "Price", "RoleId", "RoleName", "RoleRequirement", "Type"
FROM "ShopEntry";

CREATE TABLE "ef_temp_SlowmodeIgnoredRole" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SlowmodeIgnoredRole" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_SlowmodeIgnoredRole" ("Id", "DateAdded", "GuildId", "RoleId")
SELECT "Id", "DateAdded", "GuildId", "RoleId"
FROM "SlowmodeIgnoredRole";

CREATE TABLE "ef_temp_SlowmodeIgnoredUser" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SlowmodeIgnoredUser" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_SlowmodeIgnoredUser" ("Id", "DateAdded", "GuildId", "UserId")
SELECT "Id", "DateAdded", "GuildId", "UserId"
FROM "SlowmodeIgnoredUser";

CREATE TABLE "ef_temp_StreamRoleSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StreamRoleSettings" PRIMARY KEY AUTOINCREMENT,
    "AddRoleId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "Enabled" INTEGER NOT NULL,
    "FromRoleId" INTEGER NOT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "Keyword" TEXT NULL
);

INSERT INTO "ef_temp_StreamRoleSettings" ("Id", "AddRoleId", "DateAdded", "Enabled", "FromRoleId", "GuildConfigId", "GuildId", "Keyword")
SELECT "Id", "AddRoleId", "DateAdded", "Enabled", "FromRoleId", "GuildConfigId", "GuildId", "Keyword"
FROM "StreamRoleSettings";

CREATE TABLE "ef_temp_UnbanTimer" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UnbanTimer" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "UnbanAt" TEXT NOT NULL,
    "UserId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_UnbanTimer" ("Id", "DateAdded", "GuildId", "UnbanAt", "UserId")
SELECT "Id", "DateAdded", "GuildId", "UnbanAt", "UserId"
FROM "UnbanTimer";

CREATE TABLE "ef_temp_UnmuteTimer" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UnmuteTimer" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "UnmuteAt" TEXT NOT NULL,
    "UserId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_UnmuteTimer" ("Id", "DateAdded", "GuildId", "UnmuteAt", "UserId")
SELECT "Id", "DateAdded", "GuildId", "UnmuteAt", "UserId"
FROM "UnmuteTimer";

CREATE TABLE "ef_temp_UnroleTimer" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UnroleTimer" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "UnbanAt" TEXT NOT NULL,
    "UserId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_UnroleTimer" ("Id", "DateAdded", "GuildId", "RoleId", "UnbanAt", "UserId")
SELECT "Id", "DateAdded", "GuildId", "RoleId", "UnbanAt", "UserId"
FROM "UnroleTimer";

CREATE TABLE "ef_temp_VcRoleInfo" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_VcRoleInfo" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "VoiceChannelId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_VcRoleInfo" ("Id", "DateAdded", "GuildId", "RoleId", "VoiceChannelId")
SELECT "Id", "DateAdded", "GuildId", "RoleId", "VoiceChannelId"
FROM "VcRoleInfo";

CREATE TABLE "ef_temp_XpSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpSettings" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildConfigId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "ServerExcluded" INTEGER NOT NULL
);

INSERT INTO "ef_temp_XpSettings" ("Id", "DateAdded", "GuildConfigId", "GuildId", "ServerExcluded")
SELECT "Id", "DateAdded", "GuildConfigId", "GuildId", "ServerExcluded"
FROM "XpSettings";

CREATE TABLE "ef_temp_GuildConfigs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GuildConfigs" PRIMARY KEY AUTOINCREMENT,
    "AutoAssignRoleIds" TEXT NULL,
    "CleverbotEnabled" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "DeleteMessageOnCommand" INTEGER NOT NULL,
    "DeleteStreamOnlineMessage" INTEGER NOT NULL,
    "DisableGlobalExpressions" INTEGER NOT NULL,
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

INSERT INTO "ef_temp_GuildConfigs" ("Id", "AutoAssignRoleIds", "CleverbotEnabled", "DateAdded", "DeleteMessageOnCommand", "DeleteStreamOnlineMessage", "DisableGlobalExpressions", "GameVoiceChannel", "GuildId", "Locale", "MuteRoleName", "NotifyStreamOffline", "PermissionRole", "Prefix", "StickyRoles", "TimeZoneId", "VerboseErrors", "VerbosePermissions", "WarnExpireAction", "WarnExpireHours", "WarningsInitialized")
SELECT "Id", "AutoAssignRoleIds", "CleverbotEnabled", "DateAdded", "DeleteMessageOnCommand", "DeleteStreamOnlineMessage", "DisableGlobalExpressions", "GameVoiceChannel", "GuildId", "Locale", "MuteRoleName", "NotifyStreamOffline", "PermissionRole", "Prefix", "StickyRoles", "TimeZoneId", "VerboseErrors", "VerbosePermissions", "WarnExpireAction", "WarnExpireHours", "WarningsInitialized"
FROM "GuildConfigs";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;

DROP TABLE "AntiAltSetting";

ALTER TABLE "ef_temp_AntiAltSetting" RENAME TO "AntiAltSetting";

DROP TABLE "AntiRaidSetting";

ALTER TABLE "ef_temp_AntiRaidSetting" RENAME TO "AntiRaidSetting";

DROP TABLE "AntiSpamSetting";

ALTER TABLE "ef_temp_AntiSpamSetting" RENAME TO "AntiSpamSetting";

DROP TABLE "CommandAlias";

ALTER TABLE "ef_temp_CommandAlias" RENAME TO "CommandAlias";

DROP TABLE "CommandCooldown";

ALTER TABLE "ef_temp_CommandCooldown" RENAME TO "CommandCooldown";

DROP TABLE "DelMsgOnCmdChannel";

ALTER TABLE "ef_temp_DelMsgOnCmdChannel" RENAME TO "DelMsgOnCmdChannel";

DROP TABLE "ExcludedItem";

ALTER TABLE "ef_temp_ExcludedItem" RENAME TO "ExcludedItem";

DROP TABLE "FeedSub";

ALTER TABLE "ef_temp_FeedSub" RENAME TO "FeedSub";

DROP TABLE "FilterChannelId";

ALTER TABLE "ef_temp_FilterChannelId" RENAME TO "FilterChannelId";

DROP TABLE "FilteredWord";

ALTER TABLE "ef_temp_FilteredWord" RENAME TO "FilteredWord";

DROP TABLE "FilterLinksChannelId";

ALTER TABLE "ef_temp_FilterLinksChannelId" RENAME TO "FilterLinksChannelId";

DROP TABLE "FilterWordsChannelId";

ALTER TABLE "ef_temp_FilterWordsChannelId" RENAME TO "FilterWordsChannelId";

DROP TABLE "FollowedStream";

ALTER TABLE "ef_temp_FollowedStream" RENAME TO "FollowedStream";

DROP TABLE "GCChannelId";

ALTER TABLE "ef_temp_GCChannelId" RENAME TO "GCChannelId";

DROP TABLE "MutedUserId";

ALTER TABLE "ef_temp_MutedUserId" RENAME TO "MutedUserId";

DROP TABLE "Permissions";

ALTER TABLE "ef_temp_Permissions" RENAME TO "Permissions";

DROP TABLE "ShopEntry";

ALTER TABLE "ef_temp_ShopEntry" RENAME TO "ShopEntry";

DROP TABLE "SlowmodeIgnoredRole";

ALTER TABLE "ef_temp_SlowmodeIgnoredRole" RENAME TO "SlowmodeIgnoredRole";

DROP TABLE "SlowmodeIgnoredUser";

ALTER TABLE "ef_temp_SlowmodeIgnoredUser" RENAME TO "SlowmodeIgnoredUser";

DROP TABLE "StreamRoleSettings";

ALTER TABLE "ef_temp_StreamRoleSettings" RENAME TO "StreamRoleSettings";

DROP TABLE "UnbanTimer";

ALTER TABLE "ef_temp_UnbanTimer" RENAME TO "UnbanTimer";

DROP TABLE "UnmuteTimer";

ALTER TABLE "ef_temp_UnmuteTimer" RENAME TO "UnmuteTimer";

DROP TABLE "UnroleTimer";

ALTER TABLE "ef_temp_UnroleTimer" RENAME TO "UnroleTimer";

DROP TABLE "VcRoleInfo";

ALTER TABLE "ef_temp_VcRoleInfo" RENAME TO "VcRoleInfo";

DROP TABLE "XpSettings";

ALTER TABLE "ef_temp_XpSettings" RENAME TO "XpSettings";

DROP TABLE "GuildConfigs";

ALTER TABLE "ef_temp_GuildConfigs" RENAME TO "GuildConfigs";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;

CREATE UNIQUE INDEX "IX_AntiAltSetting_GuildId" ON "AntiAltSetting" ("GuildId");

CREATE UNIQUE INDEX "IX_AntiRaidSetting_GuildId" ON "AntiRaidSetting" ("GuildId");

CREATE UNIQUE INDEX "IX_AntiSpamSetting_GuildId" ON "AntiSpamSetting" ("GuildId");

CREATE INDEX "IX_CommandAlias_GuildId" ON "CommandAlias" ("GuildId");

CREATE UNIQUE INDEX "IX_CommandCooldown_GuildId_CommandName" ON "CommandCooldown" ("GuildId", "CommandName");

CREATE UNIQUE INDEX "IX_DelMsgOnCmdChannel_GuildId_ChannelId" ON "DelMsgOnCmdChannel" ("GuildId", "ChannelId");

CREATE INDEX "IX_ExcludedItem_XpSettingsId" ON "ExcludedItem" ("XpSettingsId");

CREATE UNIQUE INDEX "IX_FeedSub_GuildId_Url" ON "FeedSub" ("GuildId", "Url");

CREATE INDEX "IX_FilterChannelId_GuildFilterConfigId" ON "FilterChannelId" ("GuildFilterConfigId");

CREATE INDEX "IX_FilteredWord_GuildFilterConfigId" ON "FilteredWord" ("GuildFilterConfigId");

CREATE INDEX "IX_FilterLinksChannelId_GuildFilterConfigId" ON "FilterLinksChannelId" ("GuildFilterConfigId");

CREATE INDEX "IX_FilterWordsChannelId_GuildFilterConfigId" ON "FilterWordsChannelId" ("GuildFilterConfigId");

CREATE INDEX "IX_FollowedStream_GuildId_Username_Type" ON "FollowedStream" ("GuildId", "Username", "Type");

CREATE UNIQUE INDEX "IX_GCChannelId_GuildId_ChannelId" ON "GCChannelId" ("GuildId", "ChannelId");

CREATE UNIQUE INDEX "IX_MutedUserId_GuildId_UserId" ON "MutedUserId" ("GuildId", "UserId");

CREATE INDEX "IX_Permissions_GuildConfigId" ON "Permissions" ("GuildConfigId");

CREATE INDEX "IX_Permissions_GuildId" ON "Permissions" ("GuildId");

CREATE UNIQUE INDEX "IX_ShopEntry_GuildId_Index" ON "ShopEntry" ("GuildId", "Index");

CREATE UNIQUE INDEX "IX_SlowmodeIgnoredRole_GuildId_RoleId" ON "SlowmodeIgnoredRole" ("GuildId", "RoleId");

CREATE UNIQUE INDEX "IX_SlowmodeIgnoredUser_GuildId_UserId" ON "SlowmodeIgnoredUser" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_StreamRoleSettings_GuildId" ON "StreamRoleSettings" ("GuildId");

CREATE UNIQUE INDEX "IX_UnbanTimer_GuildId_UserId" ON "UnbanTimer" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_UnmuteTimer_GuildId_UserId" ON "UnmuteTimer" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_UnroleTimer_GuildId_UserId" ON "UnroleTimer" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_VcRoleInfo_GuildId_VoiceChannelId" ON "VcRoleInfo" ("GuildId", "VoiceChannelId");

CREATE UNIQUE INDEX "IX_XpSettings_GuildId" ON "XpSettings" ("GuildId");

CREATE UNIQUE INDEX "IX_GuildConfigs_GuildId" ON "GuildConfigs" ("GuildId");

CREATE INDEX "IX_GuildConfigs_WarnExpireHours" ON "GuildConfigs" ("WarnExpireHours");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250126062816_cleanup', '8.0.8');

COMMIT;

