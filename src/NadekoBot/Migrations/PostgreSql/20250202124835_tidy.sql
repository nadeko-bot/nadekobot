START TRANSACTION;
ALTER TABLE xpsettings DROP COLUMN guildconfigid;

ALTER TABLE feedsub DROP COLUMN guildconfigid;

ALTER TABLE delmsgoncmdchannel DROP COLUMN guildconfigid;

ALTER TABLE antiraidsetting DROP COLUMN guildconfigid;

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20250202124835_tidy', '9.0.1');

COMMIT;

