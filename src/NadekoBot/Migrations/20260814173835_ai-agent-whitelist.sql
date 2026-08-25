BEGIN TRANSACTION;
CREATE TABLE "AiAgentWhitelistEntry" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AiAgentWhitelistEntry" PRIMARY KEY AUTOINCREMENT,
    "ItemId" INTEGER NOT NULL,
    "Type" INTEGER NOT NULL
);

CREATE UNIQUE INDEX "IX_AiAgentWhitelistEntry_Type_ItemId" ON "AiAgentWhitelistEntry" ("Type", "ItemId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260814173835_ai-agent-whitelist', '9.0.1');

COMMIT;

