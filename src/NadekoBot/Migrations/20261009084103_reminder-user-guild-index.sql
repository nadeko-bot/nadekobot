BEGIN TRANSACTION;
CREATE INDEX "IX_Reminders_GuildId" ON "Reminders" ("GuildId");

CREATE INDEX "IX_Reminders_UserId" ON "Reminders" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261009084103_reminder-user-guild-index', '9.0.1');

COMMIT;

