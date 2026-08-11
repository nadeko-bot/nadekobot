BEGIN TRANSACTION;
CREATE TABLE "AutoThreadChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AutoThreadChannel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Mode" INTEGER NOT NULL,
    "ArchiveDurationMinutes" INTEGER NOT NULL
);

CREATE UNIQUE INDEX "IX_AutoThreadChannel_ChannelId" ON "AutoThreadChannel" ("ChannelId");

CREATE INDEX "IX_AutoThreadChannel_GuildId" ON "AutoThreadChannel" ("GuildId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260811020540_auto-thread-channels', '9.0.1');

COMMIT;

