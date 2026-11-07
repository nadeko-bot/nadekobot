BEGIN TRANSACTION;
ALTER TABLE "AutoThreadChannel" ADD "IncludeBots" INTEGER NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261107211550_auto-thread-include-bots', '9.0.1');

COMMIT;

