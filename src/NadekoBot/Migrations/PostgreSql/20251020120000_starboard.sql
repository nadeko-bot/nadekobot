BEGIN;

CREATE TABLE "StarboardSetting" (
    "Id" SERIAL PRIMARY KEY,
    "GuildId" BIGINT NOT NULL,
    "StarboardChannelId" BIGINT NULL,
    "Emoji" TEXT NULL,
    "Threshold" INTEGER NOT NULL,
    "AllowSelfStar" BOOLEAN NOT NULL,
    "AllowBotMessages" BOOLEAN NOT NULL,
    "StrictEmoji" BOOLEAN NOT NULL,
    "IsEnabled" BOOLEAN NOT NULL
);

CREATE UNIQUE INDEX "IX_StarboardSetting_GuildId" ON "StarboardSetting" ("GuildId");

CREATE TABLE "StarboardIgnoredChannel" (
    "Id" SERIAL PRIMARY KEY,
    "GuildId" BIGINT NOT NULL,
    "ChannelId" BIGINT NOT NULL
);

CREATE UNIQUE INDEX "IX_StarboardIgnoredChannel_GuildId_ChannelId" ON "StarboardIgnoredChannel" ("GuildId", "ChannelId");

CREATE TABLE "StarboardChannelOverride" (
    "Id" SERIAL PRIMARY KEY,
    "GuildId" BIGINT NOT NULL,
    "ChannelId" BIGINT NOT NULL,
    "Threshold" INTEGER NULL
);

CREATE UNIQUE INDEX "IX_StarboardChannelOverride_GuildId_ChannelId" ON "StarboardChannelOverride" ("GuildId", "ChannelId");

CREATE TABLE "StarboardMessage" (
    "Id" SERIAL PRIMARY KEY,
    "GuildId" BIGINT NOT NULL,
    "ChannelId" BIGINT NOT NULL,
    "SourceMessageId" BIGINT NOT NULL,
    "StarboardMessageId" BIGINT NULL,
    "StarCount" INTEGER NOT NULL,
    "SnapshotContent" TEXT NULL,
    "AuthorId" BIGINT NOT NULL
);

CREATE INDEX "IX_StarboardMessage_GuildId" ON "StarboardMessage" ("GuildId");
CREATE UNIQUE INDEX "IX_StarboardMessage_SourceMessageId" ON "StarboardMessage" ("SourceMessageId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020120000_starboard', '9.0.1');

COMMIT;
