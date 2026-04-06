BEGIN TRANSACTION;
ALTER TABLE "HoneyPotChannels" ADD "Action" INTEGER NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260405212317_honeypot-action', '9.0.1');

COMMIT;

