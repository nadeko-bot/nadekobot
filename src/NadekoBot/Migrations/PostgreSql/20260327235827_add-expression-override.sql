START TRANSACTION;
ALTER TABLE guildconfigs ADD expressionoverrideenabled boolean NOT NULL DEFAULT FALSE;

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20260327235827_add-expression-override', '9.0.1');

COMMIT;

