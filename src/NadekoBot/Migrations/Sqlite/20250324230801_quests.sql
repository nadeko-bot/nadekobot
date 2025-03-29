BEGIN TRANSACTION;
CREATE TABLE "UserQuest" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserQuest" PRIMARY KEY AUTOINCREMENT,
    "QuestNumber" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "QuestId" INTEGER NOT NULL,
    "Progress" INTEGER NOT NULL,
    "IsCompleted" INTEGER NOT NULL,
    "DateAssigned" TEXT NOT NULL
);

CREATE INDEX "IX_UserQuest_UserId" ON "UserQuest" ("UserId");

CREATE UNIQUE INDEX "IX_UserQuest_UserId_QuestNumber_DateAssigned" ON "UserQuest" ("UserId", "QuestNumber", "DateAssigned");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250324230801_quests', '9.0.1');

COMMIT;

