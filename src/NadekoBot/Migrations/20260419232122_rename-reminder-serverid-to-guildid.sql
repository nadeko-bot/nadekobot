BEGIN TRANSACTION;
ALTER TABLE "Reminders" RENAME COLUMN "ServerId" TO "GuildId";

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260419232122_rename-reminder-serverid-to-guildid', '9.0.1');

COMMIT;

