BEGIN TRANSACTION;
ALTER TABLE "UserFishStats" ADD "Bait" INTEGER NULL;

ALTER TABLE "UserFishStats" ADD "Pole" INTEGER NULL;

CREATE TABLE "ChannelXpConfig" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ChannelXpConfig" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "XpAmount" INTEGER NOT NULL,
    "Cooldown" REAL NOT NULL,
    CONSTRAINT "AK_ChannelXpConfig_GuildId_ChannelId" UNIQUE ("GuildId", "ChannelId")
);

CREATE TABLE "GuildXpConfig" (
    "GuildId" INTEGER NOT NULL CONSTRAINT "PK_GuildXpConfig" PRIMARY KEY AUTOINCREMENT,
    "XpAmount" INTEGER NOT NULL,
    "Cooldown" INTEGER NOT NULL,
    "XpTemplateUrl" TEXT NULL
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250225212144_xp-excl-xp-rate', '9.0.1');

COMMIT;

