BEGIN TRANSACTION;
CREATE INDEX "IX_Repeaters_GuildId" ON "Repeaters" ("GuildId");

CREATE INDEX "IX_ImageOnlyChannels_GuildId" ON "ImageOnlyChannels" ("GuildId");

CREATE INDEX "IX_Expressions_GuildId" ON "Expressions" ("GuildId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261014181311_guild-id-indexes', '9.0.1');

COMMIT;

