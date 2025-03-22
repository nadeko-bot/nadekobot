BEGIN TRANSACTION;
ALTER TABLE "AntiSpamSetting" DROP COLUMN "GuildConfigId";

ALTER TABLE "AntiAltSetting" DROP COLUMN "GuildConfigId";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250315225515_prot-delete-gcid', '9.0.1');

COMMIT;
