BEGIN TRANSACTION;
CREATE TABLE "LiveChannelConfig" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LiveChannelConfig" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Template" TEXT NOT NULL
);

CREATE INDEX "IX_LiveChannelConfig_GuildId" ON "LiveChannelConfig" ("GuildId");

CREATE UNIQUE INDEX "IX_LiveChannelConfig_GuildId_ChannelId" ON "LiveChannelConfig" ("GuildId", "ChannelId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250318160456_livechannels', '9.0.1');

COMMIT;

