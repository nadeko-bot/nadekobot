BEGIN TRANSACTION;
DROP INDEX "IX_XpRoleReward_XpSettingsId_Level";

CREATE UNIQUE INDEX "IX_XpRoleReward_XpSettingsId_Level_RoleId" ON "XpRoleReward" ("XpSettingsId", "Level", "RoleId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261031150252_xp-role-reward-multi', '9.0.1');

COMMIT;

