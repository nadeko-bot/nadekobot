START TRANSACTION;
ALTER TABLE honeypotchannels ADD action integer NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20260405212320_honeypot-action', '9.0.1');

COMMIT;

