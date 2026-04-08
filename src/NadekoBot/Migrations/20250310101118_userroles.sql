BEGIN TRANSACTION;
CREATE TABLE "UserRole" (
    "GuildId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    CONSTRAINT "PK_UserRole" PRIMARY KEY ("GuildId", "UserId", "RoleId")
);

CREATE INDEX "IX_UserRole_GuildId" ON "UserRole" ("GuildId");

CREATE INDEX "IX_UserRole_GuildId_UserId" ON "UserRole" ("GuildId", "UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20250310101118_userroles', '9.0.1');

COMMIT;

