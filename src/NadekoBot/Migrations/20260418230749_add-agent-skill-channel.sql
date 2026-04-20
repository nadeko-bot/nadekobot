BEGIN TRANSACTION;
DROP INDEX "IX_AiAgentGuildSkill_GuildId_Name";

ALTER TABLE "AiAgentGuildSkill" ADD "ChannelId" INTEGER NULL;

CREATE UNIQUE INDEX "IX_AiAgentGuildSkill_GuildId_ChannelId_Name" ON "AiAgentGuildSkill" ("GuildId", "ChannelId", "Name");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260418230749_add-agent-skill-channel', '9.0.1');

COMMIT;

