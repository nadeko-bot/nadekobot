BEGIN TRANSACTION;
DROP INDEX "IX_UnmuteTimer_GuildId_UserId";

INSERT INTO "UnmuteTimer" ("GuildId", "UserId", "UnmuteAt", "DateAdded", "Type")
SELECT "GuildId", "UserId", "UnmuteAt", "DateAdded", 0 FROM "UnmuteTimer" WHERE "Type" = 2;

UPDATE "UnmuteTimer" SET "Type" = 1 WHERE "Type" = 2;

CREATE UNIQUE INDEX "IX_UnmuteTimer_GuildId_UserId_Type" ON "UnmuteTimer" ("GuildId", "UserId", "Type");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260714184206_mute-surface-split', '9.0.1');

COMMIT;

