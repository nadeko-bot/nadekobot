START TRANSACTION;
ALTER TABLE notify ALTER COLUMN channelid DROP NOT NULL;

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20250228044141_notify-allow-origin-channel', '9.0.1');

COMMIT;

