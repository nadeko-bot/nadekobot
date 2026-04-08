PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;

DROP INDEX "IX_LineUpUser_GuildId_ChannelId_UserId";

CREATE TABLE "ef_temp_WaifuFan" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuFan" PRIMARY KEY AUTOINCREMENT,
    "DelegatedAt" TEXT NOT NULL,
    "UserId" INTEGER NOT NULL,
    "WaifuUserId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_WaifuFan" ("Id", "DelegatedAt", "UserId", "WaifuUserId")
SELECT "Id", "DelegatedAt", "UserId", "WaifuUserId"
FROM "WaifuFan";

DROP TABLE "WaifuFan";

ALTER TABLE "ef_temp_WaifuFan" RENAME TO "WaifuFan";

CREATE UNIQUE INDEX "IX_WaifuFan_UserId" ON "WaifuFan" ("UserId");

CREATE INDEX "IX_WaifuFan_WaifuUserId" ON "WaifuFan" ("WaifuUserId");

COMMIT;

PRAGMA foreign_keys = 1;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260309205415_remove-waifu-fan-leftat', '9.0.1');
