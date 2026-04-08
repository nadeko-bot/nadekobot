BEGIN TRANSACTION;
CREATE TABLE "UserFishItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserFishItem" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "ItemType" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    "IsEquipped" INTEGER NOT NULL,
    "UsesLeft" INTEGER NULL,
    "ExpiresAt" TEXT NULL
);

CREATE INDEX "IX_UserFishItem_UserId" ON "UserFishItem" ("UserId");

CREATE TABLE "ef_temp_UserFishStats" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserFishStats" PRIMARY KEY AUTOINCREMENT,
    "Skill" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_UserFishStats" ("Id", "Skill", "UserId")
SELECT "Id", "Skill", "UserId"
FROM "UserFishStats";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "UserFishStats";

ALTER TABLE "ef_temp_UserFishStats" RENAME TO "UserFishStats";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE UNIQUE INDEX "IX_UserFishStats_UserId" ON "UserFishStats" ("UserId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250327001835_fishitems', '9.0.1');

