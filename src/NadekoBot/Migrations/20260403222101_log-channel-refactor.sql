BEGIN TRANSACTION;

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

CREATE UNIQUE INDEX "IX_LogChannels_GuildId_LogType" ON "LogChannels" ("GuildId", "LogType");

CREATE UNIQUE INDEX "IX_LogIgnores_GuildId_LogItemId_ItemType" ON "LogIgnores" ("GuildId", "LogItemId", "ItemType");

-- Migrate data: LogSetting columns -> LogChannel rows
-- LogType enum: Other=0, MessageUpdated=1, MessageDeleted=2, UserJoined=3, UserLeft=4,
-- UserBanned=5, UserUnbanned=6, UserUpdated=7, ChannelCreated=8, ChannelDestroyed=9,
-- ChannelUpdated=10, UserPresence=11, VoicePresence=12, UserMuted=13, UserWarned=14,
-- ThreadDeleted=15, ThreadCreated=16

INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 0, "LogOtherId" FROM "LogSettings" WHERE "LogOtherId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 1, "MessageUpdatedId" FROM "LogSettings" WHERE "MessageUpdatedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 2, "MessageDeletedId" FROM "LogSettings" WHERE "MessageDeletedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 3, "UserJoinedId" FROM "LogSettings" WHERE "UserJoinedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 4, "UserLeftId" FROM "LogSettings" WHERE "UserLeftId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 5, "UserBannedId" FROM "LogSettings" WHERE "UserBannedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 6, "UserUnbannedId" FROM "LogSettings" WHERE "UserUnbannedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 7, "UserUpdatedId" FROM "LogSettings" WHERE "UserUpdatedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 8, "ChannelCreatedId" FROM "LogSettings" WHERE "ChannelCreatedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 9, "ChannelDestroyedId" FROM "LogSettings" WHERE "ChannelDestroyedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 10, "ChannelUpdatedId" FROM "LogSettings" WHERE "ChannelUpdatedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 11, "LogUserPresenceId" FROM "LogSettings" WHERE "LogUserPresenceId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 12, "LogVoicePresenceId" FROM "LogSettings" WHERE "LogVoicePresenceId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 13, "UserMutedId" FROM "LogSettings" WHERE "UserMutedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 14, "LogWarnsId" FROM "LogSettings" WHERE "LogWarnsId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 15, "ThreadDeletedId" FROM "LogSettings" WHERE "ThreadDeletedId" IS NOT NULL;
INSERT INTO "LogChannels" ("GuildId", "LogType", "ChannelId") SELECT "GuildId", 16, "ThreadCreatedId" FROM "LogSettings" WHERE "ThreadCreatedId" IS NOT NULL;

-- Migrate data: IgnoredLogChannels -> LogIgnores (join to get GuildId)
INSERT INTO "LogIgnores" ("GuildId", "LogItemId", "ItemType")
SELECT ls."GuildId", ilc."LogItemId", ilc."ItemType"
FROM "IgnoredLogChannels" ilc
JOIN "LogSettings" ls ON ilc."LogSettingId" = ls."Id";

DROP TABLE "IgnoredLogChannels";

DROP TABLE "LogSettings";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260403222101_log-channel-refactor', '9.0.1');

COMMIT;
