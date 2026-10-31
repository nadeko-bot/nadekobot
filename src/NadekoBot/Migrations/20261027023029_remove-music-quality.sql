BEGIN TRANSACTION;
CREATE TABLE "ef_temp_MusicPlayerSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MusicPlayerSettings" PRIMARY KEY AUTOINCREMENT,
    "AutoDisconnect" INTEGER NOT NULL,
    "AutoPlay" INTEGER NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "MusicChannelId" INTEGER NULL,
    "PlayerRepeat" INTEGER NOT NULL,
    "Volume" INTEGER NOT NULL DEFAULT 100
);

INSERT INTO "ef_temp_MusicPlayerSettings" ("Id", "AutoDisconnect", "AutoPlay", "GuildId", "MusicChannelId", "PlayerRepeat", "Volume")
SELECT "Id", "AutoDisconnect", "AutoPlay", "GuildId", "MusicChannelId", "PlayerRepeat", "Volume"
FROM "MusicPlayerSettings";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "MusicPlayerSettings";

ALTER TABLE "ef_temp_MusicPlayerSettings" RENAME TO "MusicPlayerSettings";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE UNIQUE INDEX "IX_MusicPlayerSettings_GuildId" ON "MusicPlayerSettings" ("GuildId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261027023029_remove-music-quality', '9.0.1');

