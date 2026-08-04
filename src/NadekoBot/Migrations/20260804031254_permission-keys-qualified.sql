BEGIN TRANSACTION;

-- Removes permission settings stored under bare subcommand names ("add"),
-- which matched several commands at once. Owners re-add them using full names.

DELETE FROM "DiscordPermOverrides"
WHERE "Command" IN ('ad','add','award','balance','cancel','clear','complete','delete','deposit','done','edit','end','error','excl','exclusive','groupdelete','groupname','grouprolereq','list','ok','pending','rem','remove','removeall','reroll','rolelvlreq','show','start','take','uncomplete','withdraw');

DELETE FROM "Permissions"
WHERE "SecondaryTarget" = 1
  AND "IsCustomCommand" = 0
  AND "SecondaryTargetName" IN ('ad','add','award','balance','cancel','clear','complete','delete','deposit','done','edit','end','error','excl','exclusive','groupdelete','groupname','grouprolereq','list','ok','pending','rem','remove','removeall','reroll','rolelvlreq','show','start','take','uncomplete','withdraw');

DELETE FROM "CommandCooldown"
WHERE "CommandName" IN ('ad','add','award','balance','cancel','clear','complete','delete','deposit','done','edit','end','error','excl','exclusive','groupdelete','groupname','grouprolereq','list','ok','pending','rem','remove','removeall','reroll','rolelvlreq','show','start','take','uncomplete','withdraw');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260804031254_permission-keys-qualified', '9.0.1');

COMMIT;

