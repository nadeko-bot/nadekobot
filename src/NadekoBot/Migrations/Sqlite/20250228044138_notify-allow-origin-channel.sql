BEGIN TRANSACTION;
CREATE TABLE "ef_temp_Notify" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Notify" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NULL,
    "GuildId" INTEGER NOT NULL,
    "Message" TEXT NOT NULL,
    "Type" INTEGER NOT NULL,
    CONSTRAINT "AK_Notify_GuildId_Type" UNIQUE ("GuildId", "Type")
);

INSERT INTO "ef_temp_Notify" ("Id", "ChannelId", "GuildId", "Message", "Type")
SELECT "Id", "ChannelId", "GuildId", "Message", "Type"
FROM "Notify";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "Notify";

ALTER TABLE "ef_temp_Notify" RENAME TO "Notify";

COMMIT;

PRAGMA foreign_keys = 1;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250228044138_notify-allow-origin-channel', '9.0.1');

