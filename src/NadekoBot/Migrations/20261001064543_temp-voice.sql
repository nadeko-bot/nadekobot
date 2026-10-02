BEGIN TRANSACTION;
CREATE TABLE "TempVoiceChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TempVoiceChannel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "OwnerId" INTEGER NOT NULL
);

CREATE TABLE "TempVoiceHub" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TempVoiceHub" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL
);

CREATE UNIQUE INDEX "IX_TempVoiceChannel_ChannelId" ON "TempVoiceChannel" ("ChannelId");

CREATE INDEX "IX_TempVoiceChannel_GuildId_OwnerId" ON "TempVoiceChannel" ("GuildId", "OwnerId");

CREATE UNIQUE INDEX "IX_TempVoiceHub_ChannelId" ON "TempVoiceHub" ("ChannelId");

CREATE INDEX "IX_TempVoiceHub_GuildId" ON "TempVoiceHub" ("GuildId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001064543_temp-voice', '9.0.1');

COMMIT;

