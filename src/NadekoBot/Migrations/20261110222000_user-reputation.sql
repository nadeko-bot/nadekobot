BEGIN TRANSACTION;
CREATE TABLE "UserReputation" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserReputation" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Reputation" INTEGER NOT NULL,
    "LastGivenAt" TEXT NULL
);

CREATE INDEX "IX_UserReputation_GuildId_Reputation" ON "UserReputation" ("GuildId", "Reputation");

CREATE UNIQUE INDEX "IX_UserReputation_GuildId_UserId" ON "UserReputation" ("GuildId", "UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261110222000_user-reputation', '9.0.1');

COMMIT;

