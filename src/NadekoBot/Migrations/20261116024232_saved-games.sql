BEGIN TRANSACTION;
CREATE TABLE "AnimalRaceEntry" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AnimalRaceEntry" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Amount" INTEGER NOT NULL,
    "DateAdded" TEXT NOT NULL
);

CREATE TABLE "SavedGame" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SavedGame" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Kind" INTEGER NOT NULL,
    "State" BLOB NOT NULL,
    "DateAdded" TEXT NOT NULL
);

CREATE UNIQUE INDEX "IX_AnimalRaceEntry_GuildId_UserId" ON "AnimalRaceEntry" ("GuildId", "UserId");

CREATE UNIQUE INDEX "IX_SavedGame_ChannelId_Kind" ON "SavedGame" ("ChannelId", "Kind");

CREATE INDEX "IX_SavedGame_GuildId_Kind" ON "SavedGame" ("GuildId", "Kind");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261116024232_saved-games', '9.0.1');

COMMIT;

