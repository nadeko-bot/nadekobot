BEGIN TRANSACTION;
DELETE FROM "WaifuCycle";
DELETE FROM "WaifuCycleSnapshot";
DELETE FROM "WaifuPendingPayout";
UPDATE "WaifuInfo" SET "TotalProduced" = 0;

ALTER TABLE "WaifuCycle" RENAME COLUMN "WaifuEarnings" TO "WaifuFeePercent";

ALTER TABLE "WaifuCycle" RENAME COLUMN "TotalReturns" TO "ReturnsCap";

ALTER TABLE "WaifuCycle" RENAME COLUMN "MoodSnapshot" TO "Processed";

ALTER TABLE "WaifuCycle" RENAME COLUMN "ManagerEarnings" TO "Price";

ALTER TABLE "WaifuCycle" ADD "ManagerCutPercent" REAL NOT NULL DEFAULT 0.0;

ALTER TABLE "BanTemplates" ADD "DisableUnban" INTEGER NOT NULL DEFAULT 0;

CREATE INDEX "IX_WaifuInfo_ManagerUserId" ON "WaifuInfo" ("ManagerUserId");

CREATE INDEX "IX_WaifuCycle_CycleNumber_Processed" ON "WaifuCycle" ("CycleNumber", "Processed");

CREATE TABLE "ef_temp_WaifuCycle" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuCycle" PRIMARY KEY AUTOINCREMENT,
    "CycleNumber" INTEGER NOT NULL,
    "ManagerCutPercent" REAL NOT NULL,
    "ManagerUserId" INTEGER NOT NULL,
    "Price" INTEGER NOT NULL,
    "Processed" INTEGER NOT NULL,
    "ProcessedAt" TEXT NULL,
    "ReturnsCap" INTEGER NOT NULL,
    "TotalBacked" INTEGER NOT NULL,
    "WaifuFeePercent" INTEGER NOT NULL,
    "WaifuUserId" INTEGER NOT NULL
);

INSERT INTO "ef_temp_WaifuCycle" ("Id", "CycleNumber", "ManagerCutPercent", "ManagerUserId", "Price", "Processed", "ProcessedAt", "ReturnsCap", "TotalBacked", "WaifuFeePercent", "WaifuUserId")
SELECT "Id", "CycleNumber", "ManagerCutPercent", "ManagerUserId", "Price", "Processed", "ProcessedAt", "ReturnsCap", "TotalBacked", "WaifuFeePercent", "WaifuUserId"
FROM "WaifuCycle";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "WaifuCycle";

ALTER TABLE "ef_temp_WaifuCycle" RENAME TO "WaifuCycle";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_WaifuCycle_CycleNumber_Processed" ON "WaifuCycle" ("CycleNumber", "Processed");

CREATE UNIQUE INDEX "IX_WaifuCycle_WaifuUserId_CycleNumber" ON "WaifuCycle" ("WaifuUserId", "CycleNumber");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260315100908_waifu-rework-3', '9.0.1');

