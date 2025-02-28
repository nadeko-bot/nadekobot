BEGIN TRANSACTION;
DROP TABLE "ExcludedItem";

ALTER TABLE "GuildXpConfig" ADD "Id" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "GuildXpConfig" ADD "RateType" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "ChannelXpConfig" ADD "RateType" INTEGER NOT NULL DEFAULT 0;

CREATE TABLE "ef_temp_GuildXpConfig" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_GuildXpConfig" PRIMARY KEY AUTOINCREMENT,
    "Cooldown" REAL NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "RateType" INTEGER NOT NULL,
    "XpAmount" INTEGER NOT NULL,
    "XpTemplateUrl" TEXT NULL,
    CONSTRAINT "AK_GuildXpConfig_GuildId_RateType" UNIQUE ("GuildId", "RateType")
);

INSERT INTO "ef_temp_GuildXpConfig" ("Id", "Cooldown", "GuildId", "RateType", "XpAmount", "XpTemplateUrl")
SELECT "Id", "Cooldown", "GuildId", "RateType", "XpAmount", "XpTemplateUrl"
FROM "GuildXpConfig";

CREATE TABLE "ef_temp_ChannelXpConfig" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ChannelXpConfig" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "Cooldown" REAL NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "RateType" INTEGER NOT NULL,
    "XpAmount" INTEGER NOT NULL,
    CONSTRAINT "AK_ChannelXpConfig_GuildId_ChannelId_RateType" UNIQUE ("GuildId", "ChannelId", "RateType")
);

INSERT INTO "ef_temp_ChannelXpConfig" ("Id", "ChannelId", "Cooldown", "GuildId", "RateType", "XpAmount")
SELECT "Id", "ChannelId", "Cooldown", "GuildId", "RateType", "XpAmount"
FROM "ChannelXpConfig";

CREATE TABLE "ef_temp_XpSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpSettings" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_XpSettings" ("Id", "DateAdded", "GuildId")
SELECT "Id", "DateAdded", "GuildId"
FROM "XpSettings";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "GuildXpConfig";

ALTER TABLE "ef_temp_GuildXpConfig" RENAME TO "GuildXpConfig";

DROP TABLE "ChannelXpConfig";

ALTER TABLE "ef_temp_ChannelXpConfig" RENAME TO "ChannelXpConfig";

DROP TABLE "XpSettings";

ALTER TABLE "ef_temp_XpSettings" RENAME TO "XpSettings";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE UNIQUE INDEX "IX_XpSettings_GuildId" ON "XpSettings" ("GuildId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250226215156_xp-rate-rework', '9.0.1');

