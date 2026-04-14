BEGIN TRANSACTION;
CREATE TABLE "UserNotifyBlock" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserNotifyBlock" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Type" TEXT NOT NULL
);

CREATE UNIQUE INDEX "IX_UserNotifyBlock_UserId_Type" ON "UserNotifyBlock" ("UserId", "Type");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260414193207_user-notify-string-key', '9.0.1');

COMMIT;

