START TRANSACTION;

ALTER TABLE antialtsetting DROP CONSTRAINT fk_antialtsetting_guildconfigs_guildconfigid;

ALTER TABLE antiraidsetting DROP CONSTRAINT fk_antiraidsetting_guildconfigs_guildconfigid;

ALTER TABLE antispamsetting DROP CONSTRAINT fk_antispamsetting_guildconfigs_guildconfigid;

ALTER TABLE commandalias DROP CONSTRAINT fk_commandalias_guildconfigs_guildconfigid;

ALTER TABLE commandcooldown DROP CONSTRAINT fk_commandcooldown_guildconfigs_guildconfigid;

ALTER TABLE delmsgoncmdchannel DROP CONSTRAINT fk_delmsgoncmdchannel_guildconfigs_guildconfigid;

ALTER TABLE excludeditem DROP CONSTRAINT fk_excludeditem_xpsettings_xpsettingsid;

ALTER TABLE feedsub DROP CONSTRAINT fk_feedsub_guildconfigs_guildconfigid;

ALTER TABLE filterchannelid DROP CONSTRAINT fk_filterchannelid_guildconfigs_guildconfigid;

ALTER TABLE filteredword DROP CONSTRAINT fk_filteredword_guildconfigs_guildconfigid;

ALTER TABLE filterlinkschannelid DROP CONSTRAINT fk_filterlinkschannelid_guildconfigs_guildconfigid;

ALTER TABLE filterwordschannelid DROP CONSTRAINT fk_filterwordschannelid_guildconfigs_guildconfigid;

ALTER TABLE followedstream DROP CONSTRAINT fk_followedstream_guildconfigs_guildconfigid;

ALTER TABLE gcchannelid DROP CONSTRAINT fk_gcchannelid_guildconfigs_guildconfigid;

ALTER TABLE muteduserid DROP CONSTRAINT fk_muteduserid_guildconfigs_guildconfigid;

ALTER TABLE permissions DROP CONSTRAINT fk_permissions_guildconfigs_guildconfigid;

ALTER TABLE shopentry DROP CONSTRAINT fk_shopentry_guildconfigs_guildconfigid;

ALTER TABLE slowmodeignoredrole DROP CONSTRAINT fk_slowmodeignoredrole_guildconfigs_guildconfigid;

ALTER TABLE slowmodeignoreduser DROP CONSTRAINT fk_slowmodeignoreduser_guildconfigs_guildconfigid;

ALTER TABLE streamrolesettings DROP CONSTRAINT fk_streamrolesettings_guildconfigs_guildconfigid;

ALTER TABLE unbantimer DROP CONSTRAINT fk_unbantimer_guildconfigs_guildconfigid;

ALTER TABLE unmutetimer DROP CONSTRAINT fk_unmutetimer_guildconfigs_guildconfigid;

ALTER TABLE unroletimer DROP CONSTRAINT fk_unroletimer_guildconfigs_guildconfigid;

ALTER TABLE vcroleinfo DROP CONSTRAINT fk_vcroleinfo_guildconfigs_guildconfigid;

ALTER TABLE xpsettings DROP CONSTRAINT fk_xpsettings_guildconfigs_guildconfigid;

DROP INDEX ix_xpsettings_guildconfigid;

DROP INDEX ix_vcroleinfo_guildconfigid;

DROP INDEX ix_unroletimer_guildconfigid;

DROP INDEX ix_unmutetimer_guildconfigid;

DROP INDEX ix_unbantimer_guildconfigid;

DROP INDEX ix_streamrolesettings_guildconfigid;

DROP INDEX ix_slowmodeignoreduser_guildconfigid;

DROP INDEX ix_slowmodeignoredrole_guildconfigid;

DROP INDEX ix_shopentry_guildconfigid;

DROP INDEX ix_muteduserid_guildconfigid;

DROP INDEX ix_gcchannelid_guildconfigid;

DROP INDEX ix_followedstream_guildconfigid;

ALTER TABLE feedsub DROP CONSTRAINT ak_feedsub_guildconfigid_url;

DROP INDEX ix_delmsgoncmdchannel_guildconfigid;

DROP INDEX ix_commandcooldown_guildconfigid;

DROP INDEX ix_commandalias_guildconfigid;

DROP INDEX ix_antispamsetting_guildconfigid;

DROP INDEX ix_antiraidsetting_guildconfigid;

DROP INDEX ix_antialtsetting_guildconfigid;

ALTER TABLE filterwordschannelid RENAME COLUMN guildconfigid TO guildfilterconfigid;

ALTER INDEX ix_filterwordschannelid_guildconfigid RENAME TO ix_filterwordschannelid_guildfilterconfigid;

ALTER TABLE filterlinkschannelid RENAME COLUMN guildconfigid TO guildfilterconfigid;

ALTER INDEX ix_filterlinkschannelid_guildconfigid RENAME TO ix_filterlinkschannelid_guildfilterconfigid;

ALTER TABLE filteredword RENAME COLUMN guildconfigid TO guildfilterconfigid;

ALTER INDEX ix_filteredword_guildconfigid RENAME TO ix_filteredword_guildfilterconfigid;

ALTER TABLE filterchannelid RENAME COLUMN guildconfigid TO guildfilterconfigid;

ALTER INDEX ix_filterchannelid_guildconfigid RENAME TO ix_filterchannelid_guildfilterconfigid;

ALTER TABLE xpsettings ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE vcroleinfo ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE unroletimer ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE unmutetimer ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE unbantimer ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE streamrolesettings ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE slowmodeignoreduser ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE slowmodeignoredrole ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE shopentry ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE permissions ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE muteduserid ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE gcchannelid ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE feedsub ALTER COLUMN url DROP NOT NULL;

ALTER TABLE feedsub ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE delmsgoncmdchannel ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE commandcooldown ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE commandalias ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE antispamsetting ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE antiraidsetting ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

ALTER TABLE antialtsetting ADD guildid numeric(20,0) NOT NULL DEFAULT 0.0;

CREATE TABLE guildfilterconfig (
    id integer GENERATED BY DEFAULT AS IDENTITY,
    guildid numeric(20,0) NOT NULL,
    filterinvites boolean NOT NULL,
    filterlinks boolean NOT NULL,
    filterwords boolean NOT NULL,
    CONSTRAINT pk_guildfilterconfig PRIMARY KEY (id)
);

insert into guildfilterconfig (Id, GuildId, FilterLinks, FilterInvites, FilterWords)
select Id, GuildId, FilterLinks, FilterInvites, FilterWords
from guildconfigs;

DELETE FROM XPSettings WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE XpSettings
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = XpSettings.GuildConfigId);

DELETE FROM StreamRoleSettings WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE StreamRoleSettings
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = StreamRoleSettings.GuildConfigId);

DELETE FROM FeedSub WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE FeedSub
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = FeedSub.GuildConfigId);

DELETE FROM DelMsgOnCmdChannel WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE DelMsgOnCmdChannel
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = DelMsgOnCmdChannel.GuildConfigId);

DELETE FROM AntiSpamSetting WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE AntiSpamSetting
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = AntiSpamSetting.GuildConfigId);

DELETE FROM AntiRaidSetting WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE AntiRaidSetting
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = AntiRaidSetting.GuildConfigId);

DELETE FROM AntiAltSetting WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE AntiAltSetting
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = AntiAltSetting.GuildConfigId);

DELETE FROM VcRoleInfo WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE VcRoleInfo
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = VcRoleInfo.GuildConfigId);

DELETE FROM UnroleTimer WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE UnroleTimer
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = UnroleTimer.GuildConfigId);

DELETE FROM UnmuteTimer WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE UnmuteTimer
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = UnmuteTimer.GuildConfigId);

DELETE FROM UnbanTimer WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE UnbanTimer
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = UnbanTimer.GuildConfigId);

DELETE FROM SlowmodeIgnoredUser WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE SlowmodeIgnoredUser
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = SlowmodeIgnoredUser.GuildConfigId);

DELETE FROM SlowmodeIgnoredRole WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE SlowmodeIgnoredRole
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = SlowmodeIgnoredRole.GuildConfigId);

DELETE FROM ShopEntry WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE ShopEntry
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = ShopEntry.GuildConfigId);

DELETE FROM Permissions WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE Permissions
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = Permissions.GuildConfigId);

DELETE FROM MutedUserId WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE MutedUserId
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = MutedUserId.GuildConfigId);

DELETE FROM GcChannelId WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE GcChannelId
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = GcChannelId.GuildConfigId);

DELETE FROM CommandCooldown WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE CommandCooldown
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = CommandCooldown.GuildConfigId);

DELETE FROM CommandAlias WHERE GuildConfigId IS NULL OR GuildConfigId NOT IN (SELECT Id FROM GuildConfigs);
UPDATE CommandAlias
SET GuildId = (SELECT GuildId FROM GuildConfigs WHERE GuildConfigs.Id = CommandAlias.GuildConfigId);

ALTER TABLE vcroleinfo DROP COLUMN guildconfigid;

ALTER TABLE unroletimer DROP COLUMN guildconfigid;

ALTER TABLE unmutetimer DROP COLUMN guildconfigid;

ALTER TABLE unbantimer DROP COLUMN guildconfigid;

ALTER TABLE slowmodeignoreduser DROP COLUMN guildconfigid;

ALTER TABLE slowmodeignoredrole DROP COLUMN guildconfigid;

ALTER TABLE shopentry DROP COLUMN guildconfigid;

ALTER TABLE muteduserid DROP COLUMN guildconfigid;

ALTER TABLE guildconfigs DROP COLUMN autodeleteselfassignedrolemessages;

ALTER TABLE guildconfigs DROP COLUMN exclusiveselfassignedroles;

ALTER TABLE guildconfigs DROP COLUMN filterinvites;

ALTER TABLE guildconfigs DROP COLUMN filterlinks;

ALTER TABLE guildconfigs DROP COLUMN filterwords;

ALTER TABLE gcchannelid DROP COLUMN guildconfigid;

ALTER TABLE followedstream DROP COLUMN dateadded;

ALTER TABLE followedstream DROP COLUMN guildconfigid;

ALTER TABLE filterchannelid DROP COLUMN dateadded;

ALTER TABLE commandcooldown DROP COLUMN guildconfigid;

ALTER TABLE commandalias DROP COLUMN guildconfigid;

CREATE UNIQUE INDEX ix_xpsettings_guildid ON xpsettings (guildid);

CREATE UNIQUE INDEX ix_xpcurrencyreward_level_xpsettingsid ON xpcurrencyreward (level, xpsettingsid);

CREATE UNIQUE INDEX ix_vcroleinfo_guildid_voicechannelid ON vcroleinfo (guildid, voicechannelid);

CREATE UNIQUE INDEX ix_unroletimer_guildid_userid ON unroletimer (guildid, userid);

CREATE UNIQUE INDEX ix_unmutetimer_guildid_userid ON unmutetimer (guildid, userid);

CREATE UNIQUE INDEX ix_unbantimer_guildid_userid ON unbantimer (guildid, userid);

CREATE UNIQUE INDEX ix_streamrolesettings_guildid ON streamrolesettings (guildid);

CREATE UNIQUE INDEX ix_slowmodeignoreduser_guildid_userid ON slowmodeignoreduser (guildid, userid);

CREATE UNIQUE INDEX ix_slowmodeignoredrole_guildid_roleid ON slowmodeignoredrole (guildid, roleid);

CREATE UNIQUE INDEX ix_shopentry_guildid_index ON shopentry (guildid, index);

CREATE INDEX ix_permissions_guildid ON permissions (guildid);

CREATE UNIQUE INDEX ix_muteduserid_guildid_userid ON muteduserid (guildid, userid);

CREATE UNIQUE INDEX ix_gcchannelid_guildid_channelid ON gcchannelid (guildid, channelid);

CREATE INDEX ix_followedstream_guildid_username_type ON followedstream (guildid, username, type);

CREATE UNIQUE INDEX ix_feedsub_guildid_url ON feedsub (guildid, url);

CREATE UNIQUE INDEX ix_delmsgoncmdchannel_guildid_channelid ON delmsgoncmdchannel (guildid, channelid);

CREATE UNIQUE INDEX ix_commandcooldown_guildid_commandname ON commandcooldown (guildid, commandname);

CREATE INDEX ix_commandalias_guildid ON commandalias (guildid);

CREATE UNIQUE INDEX ix_antispamsetting_guildid ON antispamsetting (guildid);

CREATE UNIQUE INDEX ix_antiraidsetting_guildid ON antiraidsetting (guildid);

CREATE UNIQUE INDEX ix_antialtsetting_guildid ON antialtsetting (guildid);

CREATE INDEX ix_guildfilterconfig_guildid ON guildfilterconfig (guildid);

ALTER TABLE excludeditem ADD CONSTRAINT fk_excludeditem_xpsettings_xpsettingsid FOREIGN KEY (xpsettingsid) REFERENCES xpsettings (id);

ALTER TABLE filterchannelid ADD CONSTRAINT fk_filterchannelid_guildfilterconfig_guildfilterconfigid FOREIGN KEY (guildfilterconfigid) REFERENCES guildfilterconfig (id);

ALTER TABLE filteredword ADD CONSTRAINT fk_filteredword_guildfilterconfig_guildfilterconfigid FOREIGN KEY (guildfilterconfigid) REFERENCES guildfilterconfig (id);

ALTER TABLE filterlinkschannelid ADD CONSTRAINT fk_filterlinkschannelid_guildfilterconfig_guildfilterconfigid FOREIGN KEY (guildfilterconfigid) REFERENCES guildfilterconfig (id);

ALTER TABLE filterwordschannelid ADD CONSTRAINT fk_filterwordschannelid_guildfilterconfig_guildfilterconfigid FOREIGN KEY (guildfilterconfigid) REFERENCES guildfilterconfig (id);

ALTER TABLE permissions ADD CONSTRAINT fk_permissions_guildconfigs_guildconfigid FOREIGN KEY (guildconfigid) REFERENCES guildconfigs (id);

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20250126062816_cleanup', '8.0.8');

COMMIT;

