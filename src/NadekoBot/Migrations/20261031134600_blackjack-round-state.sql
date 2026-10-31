BEGIN TRANSACTION;
CREATE TABLE "BlackjackRoundState" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_BlackjackRoundState" PRIMARY KEY AUTOINCREMENT,
    "GuildId" INTEGER NOT NULL,
    "ChannelId" INTEGER NOT NULL,
    "Cards" BLOB NOT NULL,
    "DateAdded" TEXT NOT NULL
);

CREATE UNIQUE INDEX "IX_BlackjackRoundState_ChannelId" ON "BlackjackRoundState" ("ChannelId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261031134600_blackjack-round-state', '9.0.1');

COMMIT;

