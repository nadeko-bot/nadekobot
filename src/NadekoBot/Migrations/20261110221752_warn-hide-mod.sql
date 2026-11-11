BEGIN TRANSACTION;
CREATE TABLE "HiddenWarnModGuild" (
    "GuildId" INTEGER NOT NULL CONSTRAINT "PK_HiddenWarnModGuild" PRIMARY KEY
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261110221752_warn-hide-mod', '9.0.1');

COMMIT;

