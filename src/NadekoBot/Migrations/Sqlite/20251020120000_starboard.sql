BEGIN TRANSACTION;

CREATE TABLE "StarboardSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardSetting" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "StarboardChannelId" INTEGER NULL,
    "Emoji" TEXT NULL,
    "Threshold" INTEGER NOT NULL,
    "AllowSelfStar" INTEGER NOT NULL,
    "AllowBotMessages" INTEGER NOT NULL,
    "StrictEmoji" INTEGER NOT NULL,
    "IsEnabled" INTEGER NOT NULL
);

CREATE UNIQUE INDEX "IX_StarboardSettings_GuildId" ON "StarboardSettings" ("GuildId");

CREATE TABLE "StarboardIgnoredChannels" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardIgnoredChannel" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL
);

CREATE UNIQUE INDEX "IX_StarboardIgnoredChannels_GuildId_ChannelId" ON "StarboardIgnoredChannels" ("GuildId", "ChannelId");

CREATE TABLE "StarboardChannelOverrides" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardChannelOverride" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Threshold" INTEGER NULL
);

CREATE UNIQUE INDEX "IX_StarboardChannelOverrides_GuildId_ChannelId" ON "StarboardChannelOverrides" ("GuildId", "ChannelId");

CREATE TABLE "StarboardMessages" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardMessage" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "SourceMessageId" INTEGER NOT NULL,
    "StarboardMessageId" INTEGER NULL,
    "StarCount" INTEGER NOT NULL,
    "SnapshotContent" TEXT NULL,
    "AuthorId" INTEGER NOT NULL
);

CREATE INDEX "IX_StarboardMessages_GuildId" ON "StarboardMessages" ("GuildId");
CREATE UNIQUE INDEX "IX_StarboardMessages_SourceMessageId" ON "StarboardMessages" ("SourceMessageId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020120000_starboard', '9.0.1');

COMMIT;
