PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;

-- ============================================================
-- STEP 0: Save old waifu data before destructive schema changes
-- ============================================================

-- Save all waifus with resolved Discord User IDs
CREATE TABLE "_old_waifus" (
    "OldId" INTEGER,
    "DiscordUserId" INTEGER,
    "OldPrice" INTEGER,
    "ClaimerDiscordUserId" INTEGER,
    "AffinityDiscordUserId" INTEGER
);

INSERT INTO "_old_waifus" ("OldId", "DiscordUserId", "OldPrice", "ClaimerDiscordUserId", "AffinityDiscordUserId")
SELECT wi."Id", du_waifu."UserId", wi."Price", du_claimer."UserId", du_affinity."UserId"
FROM "WaifuInfo" wi
INNER JOIN "DiscordUser" du_waifu ON du_waifu."Id" = wi."WaifuId"
LEFT JOIN "DiscordUser" du_claimer ON du_claimer."Id" = wi."ClaimerId"
LEFT JOIN "DiscordUser" du_affinity ON du_affinity."Id" = wi."AffinityId";

-- Save gift items grouped by waifu + item name (all waifus)
CREATE TABLE "_old_gifts" (
    "WaifuDiscordUserId" INTEGER,
    "ItemName" TEXT,
    "Cnt" INTEGER
);

INSERT INTO "_old_gifts" ("WaifuDiscordUserId", "ItemName", "Cnt")
SELECT du_waifu."UserId", LOWER(item."Name"), COUNT(*)
FROM "WaifuItem" item
INNER JOIN "WaifuInfo" wi ON wi."Id" = item."WaifuInfoId"
INNER JOIN "DiscordUser" du_waifu ON du_waifu."Id" = wi."WaifuId"
GROUP BY du_waifu."UserId", LOWER(item."Name");

-- ============================================================
-- STEP 1: Auto-generated schema migration (from EF Core)
-- ============================================================

DROP TABLE "WaifuItem";

DROP TABLE "WaifuUpdates";

DROP INDEX "IX_WaifuInfo_AffinityId";

DROP INDEX "IX_WaifuInfo_ClaimerId";

DROP INDEX "IX_WaifuInfo_Price";

DROP INDEX "IX_WaifuInfo_WaifuId";

ALTER TABLE "WaifuInfo" RENAME COLUMN "WaifuId" TO "WaifuFeePercent";

ALTER TABLE "WaifuInfo" RENAME COLUMN "DateAdded" TO "Quote";

ALTER TABLE "WaifuInfo" RENAME COLUMN "ClaimerId" TO "ManagerUserId";

ALTER TABLE "WaifuInfo" ADD "CustomAvatarUrl" TEXT NULL;

ALTER TABLE "WaifuInfo" ADD "Description" TEXT NULL;

ALTER TABLE "WaifuInfo" ADD "Food" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "WaifuInfo" ADD "IsHubby" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "WaifuInfo" ADD "LastDecayTime" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';

ALTER TABLE "WaifuInfo" ADD "Mood" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "WaifuInfo" ADD "ReturnsCap" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "WaifuInfo" ADD "TotalProduced" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "WaifuInfo" ADD "UserId" INTEGER NOT NULL DEFAULT 0;

DELETE FROM "WaifuInfo";

CREATE TABLE "ChannelSpotOverride" (
    "ChannelId" INTEGER NOT NULL CONSTRAINT "PK_ChannelSpotOverride" PRIMARY KEY AUTOINCREMENT,
    "Spot" INTEGER NOT NULL
);

CREATE TABLE "LineUpUser" (
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Reason" TEXT NULL,
    "DateAdded" TEXT NOT NULL,
    CONSTRAINT "PK_LineUpUser" PRIMARY KEY ("GuildId", "ChannelId", "UserId")
);

CREATE TABLE "WaifuCycle" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuCycle" PRIMARY KEY AUTOINCREMENT,
    "WaifuUserId" INTEGER NOT NULL,
    "CycleNumber" INTEGER NOT NULL,
    "ManagerUserId" INTEGER NOT NULL,
    "TotalBacked" INTEGER NOT NULL,
    "TotalReturns" INTEGER NOT NULL,
    "WaifuEarnings" INTEGER NOT NULL,
    "ManagerEarnings" INTEGER NOT NULL,
    "FanPool" INTEGER NOT NULL,
    "MoodSnapshot" INTEGER NOT NULL,
    "FoodSnapshot" INTEGER NOT NULL,
    "ProcessedAt" TEXT NOT NULL
);

CREATE TABLE "WaifuCycleSnapshot" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuCycleSnapshot" PRIMARY KEY AUTOINCREMENT,
    "CycleNumber" INTEGER NOT NULL,
    "WaifuUserId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "SnapshotBalance" INTEGER NOT NULL
);

CREATE TABLE "WaifuFan" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuFan" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "WaifuUserId" INTEGER NOT NULL,
    "DelegatedAt" TEXT NOT NULL,
    "LeftAt" TEXT NULL
);

CREATE TABLE "WaifuGiftCount" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuGiftCount" PRIMARY KEY AUTOINCREMENT,
    "WaifuUserId" INTEGER NOT NULL,
    "GiftItemId" TEXT NOT NULL,
    "Count" INTEGER NOT NULL
);

CREATE INDEX "IX_LineUpUser_GuildId_ChannelId_DateAdded" ON "LineUpUser" ("GuildId", "ChannelId", "DateAdded");

CREATE INDEX "IX_LineUpUser_GuildId_ChannelId_UserId" ON "LineUpUser" ("GuildId", "ChannelId", "UserId");

CREATE UNIQUE INDEX "IX_WaifuCycle_WaifuUserId_CycleNumber" ON "WaifuCycle" ("WaifuUserId", "CycleNumber");

CREATE INDEX "IX_WaifuCycleSnapshot_CycleNumber_WaifuUserId" ON "WaifuCycleSnapshot" ("CycleNumber", "WaifuUserId");

CREATE UNIQUE INDEX "IX_WaifuCycleSnapshot_CycleNumber_WaifuUserId_UserId" ON "WaifuCycleSnapshot" ("CycleNumber", "WaifuUserId", "UserId");

CREATE UNIQUE INDEX "IX_WaifuFan_UserId" ON "WaifuFan" ("UserId");

CREATE INDEX "IX_WaifuFan_WaifuUserId" ON "WaifuFan" ("WaifuUserId");

CREATE UNIQUE INDEX "IX_WaifuGiftCount_WaifuUserId_GiftItemId" ON "WaifuGiftCount" ("WaifuUserId", "GiftItemId");

CREATE TABLE "ef_temp_WaifuInfo" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuInfo" PRIMARY KEY AUTOINCREMENT,
    "CustomAvatarUrl" TEXT NULL,
    "Description" TEXT NULL,
    "Food" INTEGER NOT NULL,
    "IsHubby" INTEGER NOT NULL,
    "LastDecayTime" TEXT NOT NULL,
    "ManagerUserId" INTEGER NULL,
    "Mood" INTEGER NOT NULL,
    "Price" INTEGER NOT NULL,
    "Quote" TEXT NULL,
    "ReturnsCap" INTEGER NOT NULL,
    "TotalProduced" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "WaifuFeePercent" INTEGER NOT NULL
);

INSERT INTO "ef_temp_WaifuInfo" ("Id", "CustomAvatarUrl", "Description", "Food", "IsHubby", "LastDecayTime", "ManagerUserId", "Mood", "Price", "Quote", "ReturnsCap", "TotalProduced", "UserId", "WaifuFeePercent")
SELECT "Id", "CustomAvatarUrl", "Description", "Food", "IsHubby", "LastDecayTime", "ManagerUserId", "Mood", "Price", "Quote", "ReturnsCap", "TotalProduced", "UserId", "WaifuFeePercent"
FROM "WaifuInfo";

DROP TABLE "WaifuInfo";

ALTER TABLE "ef_temp_WaifuInfo" RENAME TO "WaifuInfo";

CREATE UNIQUE INDEX "IX_WaifuInfo_UserId" ON "WaifuInfo" ("UserId");

-- ============================================================
-- STEP 2: Restore migrated waifu data
-- ============================================================

DELETE FROM "WaifuInfo";

INSERT INTO "WaifuInfo" ("UserId", "Mood", "Food", "WaifuFeePercent", "Price", "ManagerUserId",
                          "ReturnsCap", "IsHubby", "LastDecayTime", "TotalProduced")
SELECT "DiscordUserId", 500, 500, 5, "OldPrice", "ClaimerDiscordUserId",
       1000000, 0, datetime('now'), 0
FROM "_old_waifus";

INSERT INTO "WaifuGiftCount" ("WaifuUserId", "GiftItemId", "Count")
SELECT "WaifuDiscordUserId",
    CASE "ItemName"
        WHEN 'cookie'     THEN '019479a1-0001-7000-8000-000000000001'
        WHEN 'donut'      THEN '019479a1-0002-7000-8000-000000000002'
        WHEN 'bread'      THEN '019479a1-0003-7000-8000-000000000003'
        WHEN 'onigiri'    THEN '019479a1-0004-7000-8000-000000000004'
        WHEN 'pizza'      THEN '019479a1-0005-7000-8000-000000000005'
        WHEN 'burger'     THEN '019479a1-0006-7000-8000-000000000006'
        WHEN 'bento'      THEN '019479a1-0007-7000-8000-000000000007'
        WHEN 'pasta'      THEN '019479a1-0008-7000-8000-000000000008'
        WHEN 'cake'       THEN '019479a1-0009-7000-8000-000000000009'
        WHEN 'sushi'      THEN '019479a1-000a-7000-8000-000000000010'
        WHEN 'lobster'    THEN '019479a1-000b-7000-8000-000000000011'
        WHEN 'feast'      THEN '019479a1-000c-7000-8000-000000000012'
        WHEN 'flower'     THEN '019479a1-1001-7000-8000-000000000101'
        WHEN 'ribbon'     THEN '019479a1-1002-7000-8000-000000000102'
        WHEN 'rose'       THEN '019479a1-1003-7000-8000-000000000103'
        WHEN 'loveletter' THEN '019479a1-1004-7000-8000-000000000104'
        WHEN 'teddy'      THEN '019479a1-1005-7000-8000-000000000105'
        WHEN 'gift'       THEN '019479a1-1006-7000-8000-000000000106'
        WHEN 'diamond'    THEN '019479a1-1007-7000-8000-000000000107'
        WHEN 'dress'      THEN '019479a1-1008-7000-8000-000000000108'
        WHEN 'piano'      THEN '019479a1-1009-7000-8000-000000000109'
        WHEN 'kitten'     THEN '019479a1-100a-7000-8000-000000000110'
        WHEN 'house'      THEN '019479a1-100b-7000-8000-000000000111'
        WHEN 'moon'       THEN '019479a1-100c-7000-8000-000000000112'
    END,
    "Cnt"
FROM "_old_gifts"
WHERE "ItemName" IN (
    'cookie', 'donut', 'bread', 'onigiri', 'pizza', 'burger',
    'bento', 'pasta', 'cake', 'sushi', 'lobster', 'feast',
    'flower', 'ribbon', 'rose', 'loveletter', 'teddy', 'gift',
    'diamond', 'dress', 'piano', 'kitten', 'house', 'moon'
);

-- Merge unmapped gift items into a single Legacy gift per waifu
INSERT INTO "WaifuGiftCount" ("WaifuUserId", "GiftItemId", "Count")
SELECT "WaifuDiscordUserId", '019479a1-ffff-7000-8000-ffffffffffff', SUM("Cnt")
FROM "_old_gifts"
WHERE "ItemName" NOT IN (
    'cookie', 'donut', 'bread', 'onigiri', 'pizza', 'burger',
    'bento', 'pasta', 'cake', 'sushi', 'lobster', 'feast',
    'flower', 'ribbon', 'rose', 'loveletter', 'teddy', 'gift',
    'diamond', 'dress', 'piano', 'kitten', 'house', 'moon'
)
GROUP BY "WaifuDiscordUserId";

-- Migrate affinity relationships as fan records
INSERT INTO "WaifuFan" ("UserId", "WaifuUserId", "DelegatedAt")
SELECT ow."DiscordUserId", ow."AffinityDiscordUserId", datetime('now')
FROM "_old_waifus" ow
WHERE ow."AffinityDiscordUserId" IS NOT NULL
AND EXISTS (SELECT 1 FROM "_old_waifus" w2 WHERE w2."DiscordUserId" = ow."AffinityDiscordUserId");

DROP TABLE "_old_waifus";
DROP TABLE "_old_gifts";

COMMIT;

PRAGMA foreign_keys = 1;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260308192112_waifu-rework', '9.0.1');
