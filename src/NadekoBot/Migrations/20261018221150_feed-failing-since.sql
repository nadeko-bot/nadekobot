BEGIN TRANSACTION;
ALTER TABLE "FeedSub" ADD "FailingSince" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261018221150_feed-failing-since', '9.0.1');

COMMIT;

