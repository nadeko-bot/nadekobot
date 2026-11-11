BEGIN TRANSACTION;
ALTER TABLE "ImageOnlyChannels" ADD "Punishment" INTEGER NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261110222918_only-channel-punishment', '9.0.1');

COMMIT;

