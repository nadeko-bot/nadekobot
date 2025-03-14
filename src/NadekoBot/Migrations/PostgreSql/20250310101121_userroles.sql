START TRANSACTION;
CREATE TABLE userrole (
    guildid numeric(20,0) NOT NULL,
    userid numeric(20,0) NOT NULL,
    roleid numeric(20,0) NOT NULL,
    CONSTRAINT pk_userrole PRIMARY KEY (guildid, userid, roleid)
);

CREATE INDEX ix_userrole_guildid ON userrole (guildid);

CREATE INDEX ix_userrole_guildid_userid ON userrole (guildid, userid);

INSERT INTO "__EFMigrationsHistory" (migrationid, productversion)
VALUES ('20250310101121_userroles', '9.0.1');

COMMIT;

