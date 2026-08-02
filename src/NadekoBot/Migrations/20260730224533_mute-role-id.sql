BEGIN TRANSACTION;
ALTER TABLE "GuildConfigs" ADD "MuteRoleId" INTEGER NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260730224533_mute-role-id', '9.0.1');

COMMIT;

