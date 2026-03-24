BEGIN TRANSACTION;
ALTER TABLE "XpSettings" ADD "XpFormulaA" INTEGER NOT NULL DEFAULT 9;

ALTER TABLE "XpSettings" ADD "XpFormulaC" INTEGER NOT NULL DEFAULT 27;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260324182616_xp-formula-config', '9.0.1');

COMMIT;
