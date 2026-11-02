BEGIN TRANSACTION;
CREATE TABLE "AllowedLinkDomain" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AllowedLinkDomain" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Domain" TEXT NOT NULL
);

CREATE UNIQUE INDEX "IX_AllowedLinkDomain_GuildId_Domain" ON "AllowedLinkDomain" ("GuildId", "Domain");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261031150943_allowed-link-domains', '9.0.1');

COMMIT;

