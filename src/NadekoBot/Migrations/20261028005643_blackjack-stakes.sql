BEGIN TRANSACTION;
CREATE TABLE "BlackjackStake" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_BlackjackStake" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Kind" INTEGER NOT NULL,
    "Amount" INTEGER NOT NULL,
    "DateAdded" TEXT NOT NULL
);

CREATE INDEX "IX_BlackjackStake_ChannelId_UserId" ON "BlackjackStake" ("ChannelId", "UserId");

CREATE INDEX "IX_BlackjackStake_UserId" ON "BlackjackStake" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261028005643_blackjack-stakes', '9.0.1');

COMMIT;

