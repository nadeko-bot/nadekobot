START TRANSACTION;

INSERT INTO logchannels (guildid, logtype, channelid)
SELECT guildid, 18, channelid FROM logchannels WHERE logtype = 7
ON CONFLICT (guildid, logtype) DO NOTHING;

INSERT INTO logchannels (guildid, logtype, channelid)
SELECT guildid, 19, channelid FROM logchannels WHERE logtype = 7
ON CONFLICT (guildid, logtype) DO NOTHING;

INSERT INTO logchannels (guildid, logtype, channelid)
SELECT guildid, 20, channelid FROM logchannels WHERE logtype = 7
ON CONFLICT (guildid, logtype) DO NOTHING;

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20260405102220_split-userupdated-log', '9.0.1');

COMMIT;

