BEGIN TRANSACTION;
CREATE TABLE "LinkFix" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LinkFix" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "OldDomain" TEXT NOT NULL,
    "NewDomain" TEXT NOT NULL
);

CREATE UNIQUE INDEX "IX_LinkFix_GuildId_OldDomain" ON "LinkFix" ("GuildId", "OldDomain");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250322225821_linkfixer', '9.0.1');

COMMIT;

