START TRANSACTION;
ALTER TABLE antispamsetting DROP COLUMN guildconfigid;

ALTER TABLE antialtsetting DROP COLUMN guildconfigid;

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20250315225518_prot-delete-gcid', '9.0.1');

COMMIT;

