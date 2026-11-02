BEGIN TRANSACTION;

-- The repeating message command is now "repeatadd"; "repeat" is a music command.

UPDATE "DiscordPermOverrides" SET "Command" = 'repeatadd' WHERE "Command" = 'repeat';

UPDATE "Permissions" SET "SecondaryTargetName" = 'repeatadd'
WHERE "SecondaryTarget" = 1
  AND "IsCustomCommand" = 0
  AND "SecondaryTargetName" = 'repeat';

UPDATE "CommandCooldown" SET "CommandName" = 'repeatadd' WHERE "CommandName" = 'repeat';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261031160156_rename-repeat-command', '9.0.1');

COMMIT;

