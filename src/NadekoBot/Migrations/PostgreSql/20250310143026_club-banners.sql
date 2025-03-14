START TRANSACTION;
ALTER TABLE clubs ADD bannerurl text;

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20250310143026_club-banners', '9.0.1');

COMMIT;

