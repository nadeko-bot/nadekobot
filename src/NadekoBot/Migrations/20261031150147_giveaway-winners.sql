BEGIN TRANSACTION;
ALTER TABLE "GiveawayModel" ADD "WinnerCount" INTEGER NOT NULL DEFAULT 1;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261031150147_giveaway-winners', '9.0.1');

COMMIT;

