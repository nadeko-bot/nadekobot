BEGIN TRANSACTION;
ALTER TABLE "GuildConfigs" ADD "ExpressionOverrideEnabled" INTEGER NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260327235821_add-expression-override', '9.0.1');

COMMIT;

