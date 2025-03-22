BEGIN TRANSACTION;
ALTER TABLE discorduser DROP COLUMN notifyonlevelup;
      
ALTER TABLE "FollowedStream" ADD "PrettyName" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250315193822_fs-prettyname', '9.0.1');

COMMIT;
