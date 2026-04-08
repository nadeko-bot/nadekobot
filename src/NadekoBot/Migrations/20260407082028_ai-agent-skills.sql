BEGIN TRANSACTION;
CREATE TABLE "AiAgentGuildSkill" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AiAgentGuildSkill" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    "Instruction" TEXT NOT NULL,
    "IsEnabled" INTEGER NOT NULL
);

CREATE INDEX "IX_AiAgentGuildSkill_GuildId" ON "AiAgentGuildSkill" ("GuildId");

CREATE UNIQUE INDEX "IX_AiAgentGuildSkill_GuildId_Name" ON "AiAgentGuildSkill" ("GuildId", "Name");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260407082028_ai-agent-skills', '9.0.1');

COMMIT;

