START TRANSACTION;
ALTER TABLE discorduser DROP COLUMN notifyonlevelup;

ALTER TABLE followedstream ADD prettyname text;

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20250315193825_fs-prettyname', '9.0.1');

COMMIT;

