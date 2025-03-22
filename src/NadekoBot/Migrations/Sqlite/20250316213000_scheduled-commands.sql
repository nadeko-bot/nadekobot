BEGIN TRANSACTION;
CREATE TABLE "ScheduledCommand" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ScheduledCommand" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "MessageId" INTEGER NOT NULL,
    "Text" TEXT NOT NULL,
    "When" TEXT NOT NULL
);

CREATE INDEX "IX_ScheduledCommand_GuildId" ON "ScheduledCommand" ("GuildId");

CREATE INDEX "IX_ScheduledCommand_UserId" ON "ScheduledCommand" ("UserId");

CREATE INDEX "IX_ScheduledCommand_When" ON "ScheduledCommand" ("When");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250316213000_scheduled-commands', '9.0.1');

COMMIT;

