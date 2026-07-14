BEGIN TRANSACTION;
DROP TABLE "UnroleTimer";

ALTER TABLE "UnmuteTimer" ADD "Type" INTEGER NOT NULL DEFAULT 2;

CREATE TABLE "ef_temp_Repeaters" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Repeaters" PRIMARY KEY AUTOINCREMENT,
    "ChannelId" INTEGER NOT NULL,
    "DateAdded" TEXT NOT NULL,
    "GuildId" INTEGER NOT NULL,
    "Interval" TEXT NULL,
    "LastMessageId" INTEGER NULL,
    "Message" TEXT NULL,
    "NoRedundant" INTEGER NOT NULL,
    "StartTimeOfDay" TEXT NULL
);

INSERT INTO "ef_temp_Repeaters" ("Id", "ChannelId", "DateAdded", "GuildId", "Interval", "LastMessageId", "Message", "NoRedundant", "StartTimeOfDay")
SELECT "Id", "ChannelId", "DateAdded", "GuildId", "Interval", "LastMessageId", "Message", "NoRedundant", "StartTimeOfDay"
FROM "Repeaters";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "Repeaters";

ALTER TABLE "ef_temp_Repeaters" RENAME TO "Repeaters";

COMMIT;

PRAGMA foreign_keys = 1;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260713234047_mute-timer-type', '9.0.1');

