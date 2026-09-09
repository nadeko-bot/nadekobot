BEGIN TRANSACTION;
CREATE INDEX "IX_Clubs_Xp" ON "Clubs" ("Xp");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260909072038_add-club-xp-index', '9.0.1');

COMMIT;

