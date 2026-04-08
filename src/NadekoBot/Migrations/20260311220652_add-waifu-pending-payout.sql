BEGIN TRANSACTION;
CREATE TABLE "WaifuPendingPayout" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WaifuPendingPayout" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Amount" TEXT NOT NULL
);

CREATE UNIQUE INDEX "IX_WaifuPendingPayout_UserId" ON "WaifuPendingPayout" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260311220652_add-waifu-pending-payout', '9.0.1');

COMMIT;

