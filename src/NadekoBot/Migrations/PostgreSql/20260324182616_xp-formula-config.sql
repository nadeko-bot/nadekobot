START TRANSACTION;
ALTER TABLE xpsettings ADD "XpFormulaA" integer NOT NULL DEFAULT 9;

ALTER TABLE xpsettings ADD "XpFormulaC" integer NOT NULL DEFAULT 27;

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20260324182616_xp-formula-config', '9.0.1');

COMMIT;
