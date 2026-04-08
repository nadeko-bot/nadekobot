BEGIN TRANSACTION;
CREATE TABLE "ef_temp_XpSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpSettings" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "ServerExcluded" INTEGER NOT NULL
);

INSERT INTO "ef_temp_XpSettings" ("Id", "DateAdded", "GuildId", "ServerExcluded")
SELECT "Id", "DateAdded", "GuildId", "ServerExcluded"
FROM "XpSettings";

CREATE TABLE "ef_temp_FeedSub" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_FeedSub" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "Message" TEXT NULL,
    "Url" TEXT NULL
);

INSERT INTO "ef_temp_FeedSub" ("Id", "ChannelId", "DateAdded", "GuildId", "Message", "Url")
SELECT "Id", "ChannelId", "DateAdded", "GuildId", "Message", "Url"
FROM "FeedSub";

CREATE TABLE "ef_temp_DelMsgOnCmdChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DelMsgOnCmdChannel" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "State" INTEGER NOT NULL
);

INSERT INTO "ef_temp_DelMsgOnCmdChannel" ("Id", "ChannelId", "DateAdded", "GuildId", "State")
SELECT "Id", "ChannelId", "DateAdded", "GuildId", "State"
FROM "DelMsgOnCmdChannel";

CREATE TABLE "ef_temp_AntiRaidSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AntiRaidSetting" PRIMARY KEY AUTOINCREMENT,
    "Action" INTEGER NOT NULL,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "PunishDuration" INTEGER NOT NULL,
    "Seconds" INTEGER NOT NULL,
    "UserThreshold" INTEGER NOT NULL
);

INSERT INTO "ef_temp_AntiRaidSetting" ("Id", "Action", "DateAdded", "GuildId", "PunishDuration", "Seconds", "UserThreshold")
SELECT "Id", "Action", "DateAdded", "GuildId", "PunishDuration", "Seconds", "UserThreshold"
FROM "AntiRaidSetting";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "XpSettings";

ALTER TABLE "ef_temp_XpSettings" RENAME TO "XpSettings";

DROP TABLE "FeedSub";

ALTER TABLE "ef_temp_FeedSub" RENAME TO "FeedSub";

DROP TABLE "DelMsgOnCmdChannel";

ALTER TABLE "ef_temp_DelMsgOnCmdChannel" RENAME TO "DelMsgOnCmdChannel";

DROP TABLE "AntiRaidSetting";

ALTER TABLE "ef_temp_AntiRaidSetting" RENAME TO "AntiRaidSetting";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE UNIQUE INDEX "IX_XpSettings_GuildId" ON "XpSettings" ("GuildId");

CREATE UNIQUE INDEX "IX_FeedSub_GuildId_Url" ON "FeedSub" ("GuildId", "Url");

CREATE UNIQUE INDEX "IX_DelMsgOnCmdChannel_GuildId_ChannelId" ON "DelMsgOnCmdChannel" ("GuildId", "ChannelId");

CREATE UNIQUE INDEX "IX_AntiRaidSetting_GuildId" ON "AntiRaidSetting" ("GuildId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250202124832_tidy', '9.0.1');

