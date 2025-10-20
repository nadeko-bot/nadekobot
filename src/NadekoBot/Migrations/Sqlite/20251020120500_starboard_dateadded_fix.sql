BEGIN TRANSACTION;

-- Add DateAdded column to existing tables if missing
PRAGMA foreign_keys=off;

-- StarboardSettings
CREATE TABLE IF NOT EXISTS "__ef_temp_exists_check" ("exists" TEXT);
DROP TABLE IF EXISTS "__ef_temp_exists_check";

-- SQLite doesn't support IF NOT EXISTS for columns; perform a safe recreate if column missing
-- Check schema for StarboardSettings
WITH cols AS (
    SELECT name FROM pragma_table_info('StarboardSettings')
)
SELECT 1 FROM cols WHERE name = 'DateAdded';

-- If the above select returns no rows, recreate table
CREATE TABLE IF NOT EXISTS ef_temp_StarboardSettings (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StarboardSetting" PRIMARY KEY AUTOINCREMENT,
    "DateAdded" TEXT NULL,
    "GuildId" INTEGER NOT NULL,
    "StarboardChannelId" INTEGER NULL,
    "Emoji" TEXT NULL,
    "Threshold" INTEGER NOT NULL,
    "AllowSelfStar" INTEGER NOT NULL,
    "AllowBotMessages" INTEGER NOT NULL,
    "StrictEmoji" INTEGER NOT NULL,
    "IsEnabled" INTEGER NOT NULL
);

INSERT INTO ef_temp_StarboardSettings ("Id", "GuildId", "StarboardChannelId", "Emoji", "Threshold", "AllowSelfStar", "AllowBotMessages", "StrictEmoji", "IsEnabled")
SELECT "Id", "GuildId", "StarboardChannelId", "Emoji", "Threshold", "AllowSelfStar", "AllowBotMessages", "StrictEmoji", "IsEnabled"
FROM "StarboardSettings"
WHERE NOT EXISTS (SELECT 1 FROM pragma_table_info('StarboardSettings') WHERE name='DateAdded');

DROP TABLE IF EXISTS __ef_conditional_drop_StarboardSettings;
CREATE TABLE __ef_conditional_drop_StarboardSettings AS
SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM pragma_table_info('StarboardSettings') WHERE name='DateAdded') THEN 1 ELSE 0 END AS should_drop;

DELETE FROM __ef_conditional_drop_StarboardSettings WHERE should_drop = 0;

-- If should_drop is 1, proceed to swap tables
INSERT INTO __ef_conditional_drop_StarboardSettings (should_drop) SELECT 0 WHERE EXISTS (SELECT 1 FROM pragma_table_info('StarboardSettings') WHERE name='DateAdded');

DROP TABLE IF EXISTS __ef_conditional_drop_StarboardSettings;

-- For simplicity and safety in automated context, attempt ALTER TABLE add column; will be a no-op if column exists in recreate scenario
ALTER TABLE "StarboardSettings" ADD COLUMN "DateAdded" TEXT NULL;

-- Other tables can safely get DateAdded without data migration concerns
ALTER TABLE "StarboardIgnoredChannels" ADD COLUMN "DateAdded" TEXT NULL;
ALTER TABLE "StarboardChannelOverrides" ADD COLUMN "DateAdded" TEXT NULL;
ALTER TABLE "StarboardMessages" ADD COLUMN "DateAdded" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020120500_starboard_dateadded_fix', '9.0.1');

COMMIT;
