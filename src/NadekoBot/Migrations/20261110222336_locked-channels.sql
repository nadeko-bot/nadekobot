BEGIN TRANSACTION;
CREATE TABLE "LockedChannel" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LockedChannel" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "AllowMask" INTEGER NOT NULL,
    "DenyMask" INTEGER NOT NULL
);

CREATE UNIQUE INDEX "IX_LockedChannel_ChannelId" ON "LockedChannel" ("ChannelId");

CREATE INDEX "IX_LockedChannel_GuildId" ON "LockedChannel" ("GuildId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261110222336_locked-channels', '9.0.1');

COMMIT;

