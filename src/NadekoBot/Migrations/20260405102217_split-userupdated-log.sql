BEGIN TRANSACTION;

INSERT OR IGNORE INTO "LogChannels" ("GuildId", "LogType", "ChannelId")
SELECT "GuildId", 18, "ChannelId" FROM "LogChannels" WHERE "LogType" = 7;

INSERT OR IGNORE INTO "LogChannels" ("GuildId", "LogType", "ChannelId")
SELECT "GuildId", 19, "ChannelId" FROM "LogChannels" WHERE "LogType" = 7;

INSERT OR IGNORE INTO "LogChannels" ("GuildId", "LogType", "ChannelId")
SELECT "GuildId", 20, "ChannelId" FROM "LogChannels" WHERE "LogType" = 7;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260405102217_split-userupdated-log', '9.0.1');

COMMIT;

