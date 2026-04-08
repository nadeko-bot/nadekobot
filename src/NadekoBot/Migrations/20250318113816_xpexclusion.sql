BEGIN TRANSACTION;
CREATE TABLE "XpExcludedItem" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_XpExcludedItem" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ItemType" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    CONSTRAINT "AK_XpExcludedItem_GuildId_ItemType_ItemId" UNIQUE ("GuildId", "ItemType", "ItemId")
);

CREATE INDEX "IX_XpExcludedItem_GuildId" ON "XpExcludedItem" ("GuildId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250318113816_xpexclusion', '9.0.1');

COMMIT;

