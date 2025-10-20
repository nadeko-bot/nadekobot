BEGIN TRANSACTION;

CREATE TABLE "StarboardSetting" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardSetting" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "StarboardChannelId" INTEGER NULL,
    "Emoji" TEXT NULL,
    "Threshold" INTEGER NOT NULL,
    "AllowSelfStar" INTEGER NOT NULL,
    "AllowBotMessages" INTEGER NOT NULL,
    "StrictEmoji" INTEGER NOT NULL,
    "IsEnabled" INTEGER NOT NULL
);

CREATE UNIQUE INDEX "IX_StarboardSetting_GuildId" ON "StarboardSetting" ("GuildId");

CREATE TABLE "StarboardIgnoredChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardIgnoredChannel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL
);

CREATE UNIQUE INDEX "IX_StarboardIgnoredChannel_GuildId_ChannelId" ON "StarboardIgnoredChannel" ("GuildId", "ChannelId");

CREATE TABLE "StarboardChannelOverride" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardChannelOverride" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Threshold" INTEGER NULL
);

CREATE UNIQUE INDEX "IX_StarboardChannelOverride_GuildId_ChannelId" ON "StarboardChannelOverride" ("GuildId", "ChannelId");

CREATE TABLE "StarboardMessage" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardMessage" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "SourceMessageId" INTEGER NOT NULL,
    "StarboardMessageId" INTEGER NULL,
    "StarCount" INTEGER NOT NULL,
    "SnapshotContent" TEXT NULL,
    "AuthorId" INTEGER NOT NULL
);

CREATE INDEX "IX_StarboardMessage_GuildId" ON "StarboardMessage" ("GuildId");
CREATE UNIQUE INDEX "IX_StarboardMessage_SourceMessageId" ON "StarboardMessage" ("SourceMessageId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020120000_starboard', '9.0.1');

COMMIT;
