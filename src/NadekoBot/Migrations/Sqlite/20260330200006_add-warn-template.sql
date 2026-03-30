BEGIN TRANSACTION;
CREATE TABLE "WarnTemplates" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WarnTemplates" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Text" TEXT NULL,
    "DateAdded" TEXT NULL
);

CREATE UNIQUE INDEX "IX_WarnTemplates_GuildId" ON "WarnTemplates" ("GuildId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260330200006_add-warn-template', '9.0.1');

COMMIT;

