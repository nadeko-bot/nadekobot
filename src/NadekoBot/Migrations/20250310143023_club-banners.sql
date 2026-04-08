BEGIN TRANSACTION;
ALTER TABLE "Clubs" ADD "BannerUrl" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250310143023_club-banners', '9.0.1');

COMMIT;

