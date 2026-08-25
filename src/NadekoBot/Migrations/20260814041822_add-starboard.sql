BEGIN TRANSACTION;
CREATE TABLE "StarboardConfig" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardConfig" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Emote" TEXT NOT NULL DEFAULT '⭐',
    "Threshold" INTEGER NOT NULL DEFAULT 3,
    "AllowSelfStar" INTEGER NOT NULL DEFAULT 0,
    "AllowBots" INTEGER NOT NULL DEFAULT 0,
    "IsEnabled" INTEGER NOT NULL DEFAULT 1,
    "Limit" INTEGER NOT NULL DEFAULT 100
);

CREATE TABLE "StarboardEntry" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardEntry" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL,
    "StarCount" INTEGER NOT NULL,
    "Position" INTEGER NOT NULL
);

CREATE TABLE "StarboardIgnoredChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardIgnoredChannel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL
);

CREATE TABLE "StarboardMessage" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardMessage" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Index" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL
);

CREATE UNIQUE INDEX "IX_StarboardConfig_GuildId" ON "StarboardConfig" ("GuildId");

CREATE UNIQUE INDEX "IX_StarboardEntry_GuildId_MessageId" ON "StarboardEntry" ("GuildId", "MessageId");

CREATE INDEX "IX_StarboardEntry_GuildId_Position" ON "StarboardEntry" ("GuildId", "Position");

CREATE UNIQUE INDEX "IX_StarboardIgnoredChannel_GuildId_ChannelId" ON "StarboardIgnoredChannel" ("GuildId", "ChannelId");

CREATE UNIQUE INDEX "IX_StarboardMessage_GuildId_Index" ON "StarboardMessage" ("GuildId", "Index");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260814041822_add-starboard', '9.0.1');

COMMIT;

