-- Permission keys are now group-qualified ("sar add" instead of "add").
-- Rows keyed by a bare subcommand name were always broken: "add" matched .sar add, .todo add
-- and .btr add alike, so there is no correct value to migrate them to. They are removed instead,
-- which returns those commands to their default permissions.
--
-- Key list is derived from data/commandlist.json: the last word of every command alias that
-- contains a space. It is not hand written, and must not be edited by hand.

BEGIN TRANSACTION;

DELETE FROM "DiscordPermOverrides"
WHERE "Command" IN ('ad','add','award','balance','cancel','clear','complete','delete','deposit','done','edit','end','error','excl','exclusive','groupdelete','groupname','grouprolereq','list','ok','pending','rem','remove','removeall','reroll','rolelvlreq','show','start','take','uncomplete','withdraw');

DELETE FROM "Permissions"
WHERE "SecondaryTarget" = 1
  AND "IsCustomCommand" = 0
  AND "SecondaryTargetName" IN ('ad','add','award','balance','cancel','clear','complete','delete','deposit','done','edit','end','error','excl','exclusive','groupdelete','groupname','grouprolereq','list','ok','pending','rem','remove','removeall','reroll','rolelvlreq','show','start','take','uncomplete','withdraw');

DELETE FROM "CommandCooldown"
WHERE "CommandName" IN ('ad','add','award','balance','cancel','clear','complete','delete','deposit','done','edit','end','error','excl','exclusive','groupdelete','groupname','grouprolereq','list','ok','pending','rem','remove','removeall','reroll','rolelvlreq','show','start','take','uncomplete','withdraw');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260730120000_permission-keys-qualified', '9.0.1');

COMMIT;
