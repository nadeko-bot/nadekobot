using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NadekoBot.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "antialtsetting",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildconfigid = table.Column<int>(type: "integer", nullable: false),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    minage = table.Column<TimeSpan>(type: "interval", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    actiondurationminutes = table.Column<int>(type: "integer", nullable: false),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_antialtsetting", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "antiraidsetting",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    userthreshold = table.Column<int>(type: "integer", nullable: false),
                    seconds = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    punishduration = table.Column<int>(type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_antiraidsetting", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "antispamsetting",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildconfigid = table.Column<int>(type: "integer", nullable: false),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    messagethreshold = table.Column<int>(type: "integer", nullable: false),
                    mutetime = table.Column<int>(type: "integer", nullable: false),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_antispamsetting", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "autocommands",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    commandtext = table.Column<string>(type: "text", nullable: true),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelname = table.Column<string>(type: "text", nullable: true),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    guildname = table.Column<string>(type: "text", nullable: true),
                    voicechannelid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    voicechannelname = table.Column<string>(type: "text", nullable: true),
                    interval = table.Column<int>(type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autocommands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "autopublishchannel",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autopublishchannel", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "autotranslatechannels",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    autodelete = table.Column<bool>(type: "boolean", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autotranslatechannels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bankusers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bankusers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bantemplates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    prunedays = table.Column<int>(type: "integer", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bantemplates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "blacklist",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    itemid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blacklist", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "buttonrole",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    buttonid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    messageid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    emote = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    exclusive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buttonrole", x => x.id);
                    table.UniqueConstraint("ak_buttonrole_roleid_messageid", x => new { x.roleid, x.messageid });
                });

            migrationBuilder.CreateTable(
                name: "commandalias",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    trigger = table.Column<string>(type: "text", nullable: true),
                    mapping = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commandalias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "commandcooldown",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    seconds = table.Column<int>(type: "integer", nullable: false),
                    commandname = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commandcooldown", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "currencytransactions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    extra = table.Column<string>(type: "text", nullable: false),
                    otherid = table.Column<decimal>(type: "numeric(20,0)", nullable: true, defaultValueSql: "NULL"),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencytransactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delmsgoncmdchannel",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    state = table.Column<bool>(type: "boolean", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delmsgoncmdchannel", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discordpermoverrides",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    perm = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    command = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discordpermoverrides", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expressions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    response = table.Column<string>(type: "text", nullable: true),
                    trigger = table.Column<string>(type: "text", nullable: true),
                    autodeletetrigger = table.Column<bool>(type: "boolean", nullable: false),
                    dmresponse = table.Column<bool>(type: "boolean", nullable: false),
                    containsanywhere = table.Column<bool>(type: "boolean", nullable: false),
                    allowtarget = table.Column<bool>(type: "boolean", nullable: false),
                    reactions = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expressions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feedsub",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    message = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedsub", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fishcatch",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    fishid = table.Column<int>(type: "integer", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    maxstars = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fishcatch", x => x.id);
                    table.UniqueConstraint("ak_fishcatch_userid_fishid", x => new { x.userid, x.fishid });
                });

            migrationBuilder.CreateTable(
                name: "flagtranslatechannel",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flagtranslatechannel", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "followedstream",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_followedstream", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gamblingstats",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    feature = table.Column<string>(type: "text", nullable: true),
                    bet = table.Column<decimal>(type: "numeric", nullable: false),
                    paidout = table.Column<decimal>(type: "numeric", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gamblingstats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gcchannelid",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gcchannelid", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "giveawaymodel",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    messageid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    message = table.Column<string>(type: "text", nullable: true),
                    endsat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_giveawaymodel", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "greetsettings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    greettype = table.Column<int>(type: "integer", nullable: false),
                    messagetext = table.Column<string>(type: "text", nullable: true),
                    isenabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    autodeletetimer = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_greetsettings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guildcolors",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    okcolor = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    errorcolor = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    pendingcolor = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guildcolors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guildconfigs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    prefix = table.Column<string>(type: "text", nullable: true),
                    deletemessageoncommand = table.Column<bool>(type: "boolean", nullable: false),
                    autoassignroleids = table.Column<string>(type: "text", nullable: true),
                    verbosepermissions = table.Column<bool>(type: "boolean", nullable: false),
                    permissionrole = table.Column<string>(type: "text", nullable: true),
                    muterolename = table.Column<string>(type: "text", nullable: true),
                    cleverbotenabled = table.Column<bool>(type: "boolean", nullable: false),
                    warningsinitialized = table.Column<bool>(type: "boolean", nullable: false),
                    gamevoicechannel = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    verboseerrors = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notifystreamoffline = table.Column<bool>(type: "boolean", nullable: false),
                    deletestreamonlinemessage = table.Column<bool>(type: "boolean", nullable: false),
                    warnexpirehours = table.Column<int>(type: "integer", nullable: false),
                    warnexpireaction = table.Column<int>(type: "integer", nullable: false),
                    disableglobalexpressions = table.Column<bool>(type: "boolean", nullable: false),
                    stickyroles = table.Column<bool>(type: "boolean", nullable: false),
                    timezoneid = table.Column<string>(type: "text", nullable: true),
                    locale = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guildconfigs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guildfilterconfig",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    filterinvites = table.Column<bool>(type: "boolean", nullable: false),
                    filterlinks = table.Column<bool>(type: "boolean", nullable: false),
                    filterwords = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guildfilterconfig", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "honeypotchannels",
                columns: table => new
                {
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_honeypotchannels", x => x.guildid);
                });

            migrationBuilder.CreateTable(
                name: "imageonlychannels",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imageonlychannels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "logsettings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    logotherid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    messageupdatedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    messagedeletedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    userjoinedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    userleftid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    userbannedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    userunbannedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    userupdatedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    channelcreatedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    channeldestroyedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    channelupdatedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    threaddeletedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    threadcreatedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    usermutedid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    loguserpresenceid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    logvoicepresenceid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    logvoicepresencettsid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    logwarnsid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logsettings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "musicplayersettings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    playerrepeat = table.Column<int>(type: "integer", nullable: false),
                    musicchannelid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    volume = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    autodisconnect = table.Column<bool>(type: "boolean", nullable: false),
                    qualitypreset = table.Column<int>(type: "integer", nullable: false),
                    autoplay = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_musicplayersettings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "musicplaylists",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: true),
                    author = table.Column<string>(type: "text", nullable: true),
                    authorid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_musicplaylists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "muteduserid",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_muteduserid", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ncpixel",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    position = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    ownerid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    color = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ncpixel", x => x.id);
                    table.UniqueConstraint("ak_ncpixel_position", x => x.position);
                });

            migrationBuilder.CreateTable(
                name: "notify",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notify", x => x.id);
                    table.UniqueConstraint("ak_notify_guildid_type", x => new { x.guildid, x.type });
                });

            migrationBuilder.CreateTable(
                name: "patrons",
                columns: table => new
                {
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    uniqueplatformuserid = table.Column<string>(type: "text", nullable: true),
                    amountcents = table.Column<int>(type: "integer", nullable: false),
                    lastcharge = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    validthru = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patrons", x => x.userid);
                });

            migrationBuilder.CreateTable(
                name: "plantedcurrency",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    password = table.Column<string>(type: "text", nullable: true),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    messageid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantedcurrency", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    keyword = table.Column<string>(type: "text", nullable: false),
                    authorname = table.Column<string>(type: "text", nullable: false),
                    authorid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rakeback",
                columns: table => new
                {
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rakeback", x => x.userid);
                });

            migrationBuilder.CreateTable(
                name: "reactionroles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    messageid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    emote = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    group = table.Column<int>(type: "integer", nullable: false),
                    levelreq = table.Column<int>(type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reactionroles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reminders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    when = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    serverid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    message = table.Column<string>(type: "text", nullable: true),
                    isprivate = table.Column<bool>(type: "boolean", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "repeaters",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    lastmessageid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    message = table.Column<string>(type: "text", nullable: true),
                    interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                    starttimeofday = table.Column<TimeSpan>(type: "interval", nullable: true),
                    noredundant = table.Column<bool>(type: "boolean", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repeaters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rewardedusers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    platformuserid = table.Column<string>(type: "text", nullable: true),
                    amountrewardedthismonth = table.Column<long>(type: "bigint", nullable: false),
                    lastreward = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rewardedusers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rotatingstatus",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    status = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rotatingstatus", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sarautodelete",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    isenabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sarautodelete", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sargroup",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    groupnumber = table.Column<int>(type: "integer", nullable: false),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    rolereq = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    isexclusive = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sargroup", x => x.id);
                    table.UniqueConstraint("ak_sargroup_guildid_groupnumber", x => new { x.guildid, x.groupnumber });
                });

            migrationBuilder.CreateTable(
                name: "shopentry",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    authorid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    rolename = table.Column<string>(type: "text", nullable: true),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    rolerequirement = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    command = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shopentry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "slowmodeignoredrole",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slowmodeignoredrole", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "slowmodeignoreduser",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slowmodeignoreduser", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stickyroles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    roleids = table.Column<string>(type: "text", nullable: true),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stickyroles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "streamonlinemessages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    messageid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streamonlinemessages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "streamrolesettings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildconfigid = table.Column<int>(type: "integer", nullable: false),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    addroleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    fromroleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    keyword = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streamrolesettings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "temprole",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    remove = table.Column<bool>(type: "boolean", nullable: false),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    expiresat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_temprole", x => x.id);
                    table.UniqueConstraint("ak_temprole_guildid_userid_roleid", x => new { x.guildid, x.userid, x.roleid });
                });

            migrationBuilder.CreateTable(
                name: "todosarchive",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_todosarchive", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unbantimer",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    unbanat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unbantimer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unmutetimer",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    unmuteat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unmutetimer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "unroletimer",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    unbanat = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unroletimer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "userbetstats",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    game = table.Column<int>(type: "integer", nullable: false),
                    wincount = table.Column<long>(type: "bigint", nullable: false),
                    losecount = table.Column<long>(type: "bigint", nullable: false),
                    totalbet = table.Column<decimal>(type: "numeric", nullable: false),
                    paidout = table.Column<decimal>(type: "numeric", nullable: false),
                    maxwin = table.Column<long>(type: "bigint", nullable: false),
                    maxbet = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userbetstats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "userfishstats",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    skill = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userfishstats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "userxpstats",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    xp = table.Column<long>(type: "bigint", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userxpstats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vcroleinfo",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    voicechannelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vcroleinfo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "warningpunishment",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    punishment = table.Column<int>(type: "integer", nullable: false),
                    time = table.Column<int>(type: "integer", nullable: false),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warningpunishment", x => x.id);
                    table.UniqueConstraint("ak_warningpunishment_guildid_count", x => new { x.guildid, x.count });
                });

            migrationBuilder.CreateTable(
                name: "warnings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    forgiven = table.Column<bool>(type: "boolean", nullable: false),
                    forgivenby = table.Column<string>(type: "text", nullable: true),
                    moderator = table.Column<string>(type: "text", nullable: true),
                    weight = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warnings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "xpsettings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    serverexcluded = table.Column<bool>(type: "boolean", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xpsettings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "xpshopowneditem",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    itemtype = table.Column<int>(type: "integer", nullable: false),
                    isusing = table.Column<bool>(type: "boolean", nullable: false),
                    itemkey = table.Column<string>(type: "text", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xpshopowneditem", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "antispamignore",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    antispamsettingid = table.Column<int>(type: "integer", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_antispamignore", x => x.id);
                    table.ForeignKey(
                        name: "fk_antispamignore_antispamsetting_antispamsettingid",
                        column: x => x.antispamsettingid,
                        principalTable: "antispamsetting",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "autotranslateusers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    channelid = table.Column<int>(type: "integer", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    source = table.Column<string>(type: "text", nullable: true),
                    target = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autotranslateusers", x => x.id);
                    table.UniqueConstraint("ak_autotranslateusers_channelid_userid", x => new { x.channelid, x.userid });
                    table.ForeignKey(
                        name: "fk_autotranslateusers_autotranslatechannels_channelid",
                        column: x => x.channelid,
                        principalTable: "autotranslatechannels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "giveawayuser",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    giveawayid = table.Column<int>(type: "integer", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_giveawayuser", x => x.id);
                    table.ForeignKey(
                        name: "fk_giveawayuser_giveawaymodel_giveawayid",
                        column: x => x.giveawayid,
                        principalTable: "giveawaymodel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    primarytarget = table.Column<int>(type: "integer", nullable: false),
                    primarytargetid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    secondarytarget = table.Column<int>(type: "integer", nullable: false),
                    secondarytargetname = table.Column<string>(type: "text", nullable: true),
                    iscustomcommand = table.Column<bool>(type: "boolean", nullable: false),
                    state = table.Column<bool>(type: "boolean", nullable: false),
                    guildconfigid = table.Column<int>(type: "integer", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_permissions_guildconfigs_guildconfigid",
                        column: x => x.guildconfigid,
                        principalTable: "guildconfigs",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "filterchannelid",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildfilterconfigid = table.Column<int>(type: "integer", nullable: true),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_filterchannelid", x => x.id);
                    table.ForeignKey(
                        name: "fk_filterchannelid_guildfilterconfig_guildfilterconfigid",
                        column: x => x.guildfilterconfigid,
                        principalTable: "guildfilterconfig",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "filteredword",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildfilterconfigid = table.Column<int>(type: "integer", nullable: true),
                    word = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_filteredword", x => x.id);
                    table.ForeignKey(
                        name: "fk_filteredword_guildfilterconfig_guildfilterconfigid",
                        column: x => x.guildfilterconfigid,
                        principalTable: "guildfilterconfig",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "filterlinkschannelid",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    guildfilterconfigid = table.Column<int>(type: "integer", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_filterlinkschannelid", x => x.id);
                    table.ForeignKey(
                        name: "fk_filterlinkschannelid_guildfilterconfig_guildfilterconfigid",
                        column: x => x.guildfilterconfigid,
                        principalTable: "guildfilterconfig",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "filterwordschannelid",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildfilterconfigid = table.Column<int>(type: "integer", nullable: true),
                    channelid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_filterwordschannelid", x => x.id);
                    table.ForeignKey(
                        name: "fk_filterwordschannelid_guildfilterconfig_guildfilterconfigid",
                        column: x => x.guildfilterconfigid,
                        principalTable: "guildfilterconfig",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "ignoredlogchannels",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    logsettingid = table.Column<int>(type: "integer", nullable: false),
                    logitemid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    itemtype = table.Column<int>(type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ignoredlogchannels", x => x.id);
                    table.ForeignKey(
                        name: "fk_ignoredlogchannels_logsettings_logsettingid",
                        column: x => x.logsettingid,
                        principalTable: "logsettings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlistsong",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    provider = table.Column<string>(type: "text", nullable: true),
                    providertype = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    uri = table.Column<string>(type: "text", nullable: true),
                    query = table.Column<string>(type: "text", nullable: true),
                    musicplaylistid = table.Column<int>(type: "integer", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlistsong", x => x.id);
                    table.ForeignKey(
                        name: "fk_playlistsong_musicplaylists_musicplaylistid",
                        column: x => x.musicplaylistid,
                        principalTable: "musicplaylists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sar",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guildid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    sargroupid = table.Column<int>(type: "integer", nullable: false),
                    levelreq = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sar", x => x.id);
                    table.UniqueConstraint("ak_sar_guildid_roleid", x => new { x.guildid, x.roleid });
                    table.ForeignKey(
                        name: "fk_sar_sargroup_sargroupid",
                        column: x => x.sargroupid,
                        principalTable: "sargroup",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shopentryitem",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text = table.Column<string>(type: "text", nullable: true),
                    shopentryid = table.Column<int>(type: "integer", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shopentryitem", x => x.id);
                    table.ForeignKey(
                        name: "fk_shopentryitem_shopentry_shopentryid",
                        column: x => x.shopentryid,
                        principalTable: "shopentry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "streamroleblacklisteduser",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    streamrolesettingsid = table.Column<int>(type: "integer", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streamroleblacklisteduser", x => x.id);
                    table.ForeignKey(
                        name: "fk_streamroleblacklisteduser_streamrolesettings_streamrolesett~",
                        column: x => x.streamrolesettingsid,
                        principalTable: "streamrolesettings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "streamrolewhitelisteduser",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    streamrolesettingsid = table.Column<int>(type: "integer", nullable: false),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streamrolewhitelisteduser", x => x.id);
                    table.ForeignKey(
                        name: "fk_streamrolewhitelisteduser_streamrolesettings_streamrolesett~",
                        column: x => x.streamrolesettingsid,
                        principalTable: "streamrolesettings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "todos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    todo = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    isdone = table.Column<bool>(type: "boolean", nullable: false),
                    archiveid = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_todos", x => x.id);
                    table.ForeignKey(
                        name: "fk_todos_todosarchive_archiveid",
                        column: x => x.archiveid,
                        principalTable: "todosarchive",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "excludeditem",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xpsettingsid = table.Column<int>(type: "integer", nullable: true),
                    itemid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    itemtype = table.Column<int>(type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_excludeditem", x => x.id);
                    table.ForeignKey(
                        name: "fk_excludeditem_xpsettings_xpsettingsid",
                        column: x => x.xpsettingsid,
                        principalTable: "xpsettings",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "xpcurrencyreward",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xpsettingsid = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xpcurrencyreward", x => x.id);
                    table.ForeignKey(
                        name: "fk_xpcurrencyreward_xpsettings_xpsettingsid",
                        column: x => x.xpsettingsid,
                        principalTable: "xpsettings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "xprolereward",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xpsettingsid = table.Column<int>(type: "integer", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    roleid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    remove = table.Column<bool>(type: "boolean", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xprolereward", x => x.id);
                    table.ForeignKey(
                        name: "fk_xprolereward_xpsettings_xpsettingsid",
                        column: x => x.xpsettingsid,
                        principalTable: "xpsettings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clubapplicants",
                columns: table => new
                {
                    clubid = table.Column<int>(type: "integer", nullable: false),
                    userid = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clubapplicants", x => new { x.clubid, x.userid });
                });

            migrationBuilder.CreateTable(
                name: "clubbans",
                columns: table => new
                {
                    clubid = table.Column<int>(type: "integer", nullable: false),
                    userid = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clubbans", x => new { x.clubid, x.userid });
                });

            migrationBuilder.CreateTable(
                name: "clubs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    imageurl = table.Column<string>(type: "text", nullable: true),
                    xp = table.Column<int>(type: "integer", nullable: false),
                    ownerid = table.Column<int>(type: "integer", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clubs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discorduser",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    avatarid = table.Column<string>(type: "text", nullable: true),
                    clubid = table.Column<int>(type: "integer", nullable: true),
                    isclubadmin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    totalxp = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    notifyonlevelup = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    currencyamount = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discorduser", x => x.id);
                    table.UniqueConstraint("ak_discorduser_userid", x => x.userid);
                    table.ForeignKey(
                        name: "fk_discorduser_clubs_clubid",
                        column: x => x.clubid,
                        principalTable: "clubs",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "waifuinfo",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    waifuid = table.Column<int>(type: "integer", nullable: false),
                    claimerid = table.Column<int>(type: "integer", nullable: true),
                    affinityid = table.Column<int>(type: "integer", nullable: true),
                    price = table.Column<long>(type: "bigint", nullable: false),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waifuinfo", x => x.id);
                    table.ForeignKey(
                        name: "fk_waifuinfo_discorduser_affinityid",
                        column: x => x.affinityid,
                        principalTable: "discorduser",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_waifuinfo_discorduser_claimerid",
                        column: x => x.claimerid,
                        principalTable: "discorduser",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_waifuinfo_discorduser_waifuid",
                        column: x => x.waifuid,
                        principalTable: "discorduser",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "waifuupdates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<int>(type: "integer", nullable: false),
                    updatetype = table.Column<int>(type: "integer", nullable: false),
                    oldid = table.Column<int>(type: "integer", nullable: true),
                    newid = table.Column<int>(type: "integer", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waifuupdates", x => x.id);
                    table.ForeignKey(
                        name: "fk_waifuupdates_discorduser_newid",
                        column: x => x.newid,
                        principalTable: "discorduser",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_waifuupdates_discorduser_oldid",
                        column: x => x.oldid,
                        principalTable: "discorduser",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_waifuupdates_discorduser_userid",
                        column: x => x.userid,
                        principalTable: "discorduser",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "waifuitem",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    waifuinfoid = table.Column<int>(type: "integer", nullable: true),
                    itememoji = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "text", nullable: true),
                    dateadded = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waifuitem", x => x.id);
                    table.ForeignKey(
                        name: "fk_waifuitem_waifuinfo_waifuinfoid",
                        column: x => x.waifuinfoid,
                        principalTable: "waifuinfo",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_antialtsetting_guildid",
                table: "antialtsetting",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_antiraidsetting_guildid",
                table: "antiraidsetting",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_antispamignore_antispamsettingid",
                table: "antispamignore",
                column: "antispamsettingid");

            migrationBuilder.CreateIndex(
                name: "ix_antispamsetting_guildid",
                table: "antispamsetting",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_autopublishchannel_guildid",
                table: "autopublishchannel",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_autotranslatechannels_channelid",
                table: "autotranslatechannels",
                column: "channelid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_autotranslatechannels_guildid",
                table: "autotranslatechannels",
                column: "guildid");

            migrationBuilder.CreateIndex(
                name: "ix_bankusers_userid",
                table: "bankusers",
                column: "userid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bantemplates_guildid",
                table: "bantemplates",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_buttonrole_guildid",
                table: "buttonrole",
                column: "guildid");

            migrationBuilder.CreateIndex(
                name: "ix_clubapplicants_userid",
                table: "clubapplicants",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_clubbans_userid",
                table: "clubbans",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_clubs_name",
                table: "clubs",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clubs_ownerid",
                table: "clubs",
                column: "ownerid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commandalias_guildid",
                table: "commandalias",
                column: "guildid");

            migrationBuilder.CreateIndex(
                name: "ix_commandcooldown_guildid_commandname",
                table: "commandcooldown",
                columns: new[] { "guildid", "commandname" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_currencytransactions_userid",
                table: "currencytransactions",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_delmsgoncmdchannel_guildid_channelid",
                table: "delmsgoncmdchannel",
                columns: new[] { "guildid", "channelid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discordpermoverrides_guildid_command",
                table: "discordpermoverrides",
                columns: new[] { "guildid", "command" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discorduser_clubid",
                table: "discorduser",
                column: "clubid");

            migrationBuilder.CreateIndex(
                name: "ix_discorduser_currencyamount",
                table: "discorduser",
                column: "currencyamount");

            migrationBuilder.CreateIndex(
                name: "ix_discorduser_totalxp",
                table: "discorduser",
                column: "totalxp");

            migrationBuilder.CreateIndex(
                name: "ix_discorduser_userid",
                table: "discorduser",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_discorduser_username",
                table: "discorduser",
                column: "username");

            migrationBuilder.CreateIndex(
                name: "ix_excludeditem_xpsettingsid",
                table: "excludeditem",
                column: "xpsettingsid");

            migrationBuilder.CreateIndex(
                name: "ix_feedsub_guildid_url",
                table: "feedsub",
                columns: new[] { "guildid", "url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_filterchannelid_guildfilterconfigid",
                table: "filterchannelid",
                column: "guildfilterconfigid");

            migrationBuilder.CreateIndex(
                name: "ix_filteredword_guildfilterconfigid",
                table: "filteredword",
                column: "guildfilterconfigid");

            migrationBuilder.CreateIndex(
                name: "ix_filterlinkschannelid_guildfilterconfigid",
                table: "filterlinkschannelid",
                column: "guildfilterconfigid");

            migrationBuilder.CreateIndex(
                name: "ix_filterwordschannelid_guildfilterconfigid",
                table: "filterwordschannelid",
                column: "guildfilterconfigid");

            migrationBuilder.CreateIndex(
                name: "ix_flagtranslatechannel_guildid_channelid",
                table: "flagtranslatechannel",
                columns: new[] { "guildid", "channelid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_followedstream_guildid_username_type",
                table: "followedstream",
                columns: new[] { "guildid", "username", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_gamblingstats_feature",
                table: "gamblingstats",
                column: "feature",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gcchannelid_guildid_channelid",
                table: "gcchannelid",
                columns: new[] { "guildid", "channelid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_giveawayuser_giveawayid_userid",
                table: "giveawayuser",
                columns: new[] { "giveawayid", "userid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_greetsettings_guildid_greettype",
                table: "greetsettings",
                columns: new[] { "guildid", "greettype" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guildcolors_guildid",
                table: "guildcolors",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guildconfigs_guildid",
                table: "guildconfigs",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guildconfigs_warnexpirehours",
                table: "guildconfigs",
                column: "warnexpirehours");

            migrationBuilder.CreateIndex(
                name: "ix_guildfilterconfig_guildid",
                table: "guildfilterconfig",
                column: "guildid");

            migrationBuilder.CreateIndex(
                name: "ix_ignoredlogchannels_logsettingid_logitemid_itemtype",
                table: "ignoredlogchannels",
                columns: new[] { "logsettingid", "logitemid", "itemtype" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_imageonlychannels_channelid",
                table: "imageonlychannels",
                column: "channelid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_logsettings_guildid",
                table: "logsettings",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_musicplayersettings_guildid",
                table: "musicplayersettings",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_muteduserid_guildid_userid",
                table: "muteduserid",
                columns: new[] { "guildid", "userid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ncpixel_ownerid",
                table: "ncpixel",
                column: "ownerid");

            migrationBuilder.CreateIndex(
                name: "ix_patrons_uniqueplatformuserid",
                table: "patrons",
                column: "uniqueplatformuserid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permissions_guildconfigid",
                table: "permissions",
                column: "guildconfigid");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_guildid",
                table: "permissions",
                column: "guildid");

            migrationBuilder.CreateIndex(
                name: "ix_plantedcurrency_channelid",
                table: "plantedcurrency",
                column: "channelid");

            migrationBuilder.CreateIndex(
                name: "ix_plantedcurrency_messageid",
                table: "plantedcurrency",
                column: "messageid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_playlistsong_musicplaylistid",
                table: "playlistsong",
                column: "musicplaylistid");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_guildid",
                table: "quotes",
                column: "guildid");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_keyword",
                table: "quotes",
                column: "keyword");

            migrationBuilder.CreateIndex(
                name: "ix_reactionroles_guildid",
                table: "reactionroles",
                column: "guildid");

            migrationBuilder.CreateIndex(
                name: "ix_reactionroles_messageid_emote",
                table: "reactionroles",
                columns: new[] { "messageid", "emote" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reminders_when",
                table: "reminders",
                column: "when");

            migrationBuilder.CreateIndex(
                name: "ix_rewardedusers_platformuserid",
                table: "rewardedusers",
                column: "platformuserid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sar_sargroupid",
                table: "sar",
                column: "sargroupid");

            migrationBuilder.CreateIndex(
                name: "ix_sarautodelete_guildid",
                table: "sarautodelete",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shopentry_guildid_index",
                table: "shopentry",
                columns: new[] { "guildid", "index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shopentryitem_shopentryid",
                table: "shopentryitem",
                column: "shopentryid");

            migrationBuilder.CreateIndex(
                name: "ix_slowmodeignoredrole_guildid_roleid",
                table: "slowmodeignoredrole",
                columns: new[] { "guildid", "roleid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_slowmodeignoreduser_guildid_userid",
                table: "slowmodeignoreduser",
                columns: new[] { "guildid", "userid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stickyroles_guildid_userid",
                table: "stickyroles",
                columns: new[] { "guildid", "userid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_streamroleblacklisteduser_streamrolesettingsid",
                table: "streamroleblacklisteduser",
                column: "streamrolesettingsid");

            migrationBuilder.CreateIndex(
                name: "ix_streamrolesettings_guildid",
                table: "streamrolesettings",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_streamrolewhitelisteduser_streamrolesettingsid",
                table: "streamrolewhitelisteduser",
                column: "streamrolesettingsid");

            migrationBuilder.CreateIndex(
                name: "ix_temprole_expiresat",
                table: "temprole",
                column: "expiresat");

            migrationBuilder.CreateIndex(
                name: "ix_todos_archiveid",
                table: "todos",
                column: "archiveid");

            migrationBuilder.CreateIndex(
                name: "ix_todos_userid",
                table: "todos",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_unbantimer_guildid_userid",
                table: "unbantimer",
                columns: new[] { "guildid", "userid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unmutetimer_guildid_userid",
                table: "unmutetimer",
                columns: new[] { "guildid", "userid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unroletimer_guildid_userid",
                table: "unroletimer",
                columns: new[] { "guildid", "userid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_userbetstats_maxwin",
                table: "userbetstats",
                column: "maxwin");

            migrationBuilder.CreateIndex(
                name: "ix_userbetstats_userid_game",
                table: "userbetstats",
                columns: new[] { "userid", "game" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_userfishstats_userid",
                table: "userfishstats",
                column: "userid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_userxpstats_guildid",
                table: "userxpstats",
                column: "guildid");

            migrationBuilder.CreateIndex(
                name: "ix_userxpstats_userid",
                table: "userxpstats",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_userxpstats_userid_guildid",
                table: "userxpstats",
                columns: new[] { "userid", "guildid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_userxpstats_xp",
                table: "userxpstats",
                column: "xp");

            migrationBuilder.CreateIndex(
                name: "ix_vcroleinfo_guildid_voicechannelid",
                table: "vcroleinfo",
                columns: new[] { "guildid", "voicechannelid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_waifuinfo_affinityid",
                table: "waifuinfo",
                column: "affinityid");

            migrationBuilder.CreateIndex(
                name: "ix_waifuinfo_claimerid",
                table: "waifuinfo",
                column: "claimerid");

            migrationBuilder.CreateIndex(
                name: "ix_waifuinfo_price",
                table: "waifuinfo",
                column: "price");

            migrationBuilder.CreateIndex(
                name: "ix_waifuinfo_waifuid",
                table: "waifuinfo",
                column: "waifuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_waifuitem_waifuinfoid",
                table: "waifuitem",
                column: "waifuinfoid");

            migrationBuilder.CreateIndex(
                name: "ix_waifuupdates_newid",
                table: "waifuupdates",
                column: "newid");

            migrationBuilder.CreateIndex(
                name: "ix_waifuupdates_oldid",
                table: "waifuupdates",
                column: "oldid");

            migrationBuilder.CreateIndex(
                name: "ix_waifuupdates_userid",
                table: "waifuupdates",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_warnings_dateadded",
                table: "warnings",
                column: "dateadded");

            migrationBuilder.CreateIndex(
                name: "ix_warnings_guildid",
                table: "warnings",
                column: "guildid");

            migrationBuilder.CreateIndex(
                name: "ix_warnings_userid",
                table: "warnings",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_xpcurrencyreward_level_xpsettingsid",
                table: "xpcurrencyreward",
                columns: new[] { "level", "xpsettingsid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_xpcurrencyreward_xpsettingsid",
                table: "xpcurrencyreward",
                column: "xpsettingsid");

            migrationBuilder.CreateIndex(
                name: "ix_xprolereward_xpsettingsid_level",
                table: "xprolereward",
                columns: new[] { "xpsettingsid", "level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_xpsettings_guildid",
                table: "xpsettings",
                column: "guildid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_xpshopowneditem_userid_itemtype_itemkey",
                table: "xpshopowneditem",
                columns: new[] { "userid", "itemtype", "itemkey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_clubapplicants_clubs_clubid",
                table: "clubapplicants",
                column: "clubid",
                principalTable: "clubs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_clubapplicants_discorduser_userid",
                table: "clubapplicants",
                column: "userid",
                principalTable: "discorduser",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_clubbans_clubs_clubid",
                table: "clubbans",
                column: "clubid",
                principalTable: "clubs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_clubbans_discorduser_userid",
                table: "clubbans",
                column: "userid",
                principalTable: "discorduser",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_clubs_discorduser_ownerid",
                table: "clubs",
                column: "ownerid",
                principalTable: "discorduser",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_discorduser_clubs_clubid",
                table: "discorduser");

            migrationBuilder.DropTable(
                name: "antialtsetting");

            migrationBuilder.DropTable(
                name: "antiraidsetting");

            migrationBuilder.DropTable(
                name: "antispamignore");

            migrationBuilder.DropTable(
                name: "autocommands");

            migrationBuilder.DropTable(
                name: "autopublishchannel");

            migrationBuilder.DropTable(
                name: "autotranslateusers");

            migrationBuilder.DropTable(
                name: "bankusers");

            migrationBuilder.DropTable(
                name: "bantemplates");

            migrationBuilder.DropTable(
                name: "blacklist");

            migrationBuilder.DropTable(
                name: "buttonrole");

            migrationBuilder.DropTable(
                name: "clubapplicants");

            migrationBuilder.DropTable(
                name: "clubbans");

            migrationBuilder.DropTable(
                name: "commandalias");

            migrationBuilder.DropTable(
                name: "commandcooldown");

            migrationBuilder.DropTable(
                name: "currencytransactions");

            migrationBuilder.DropTable(
                name: "delmsgoncmdchannel");

            migrationBuilder.DropTable(
                name: "discordpermoverrides");

            migrationBuilder.DropTable(
                name: "excludeditem");

            migrationBuilder.DropTable(
                name: "expressions");

            migrationBuilder.DropTable(
                name: "feedsub");

            migrationBuilder.DropTable(
                name: "filterchannelid");

            migrationBuilder.DropTable(
                name: "filteredword");

            migrationBuilder.DropTable(
                name: "filterlinkschannelid");

            migrationBuilder.DropTable(
                name: "filterwordschannelid");

            migrationBuilder.DropTable(
                name: "fishcatch");

            migrationBuilder.DropTable(
                name: "flagtranslatechannel");

            migrationBuilder.DropTable(
                name: "followedstream");

            migrationBuilder.DropTable(
                name: "gamblingstats");

            migrationBuilder.DropTable(
                name: "gcchannelid");

            migrationBuilder.DropTable(
                name: "giveawayuser");

            migrationBuilder.DropTable(
                name: "greetsettings");

            migrationBuilder.DropTable(
                name: "guildcolors");

            migrationBuilder.DropTable(
                name: "honeypotchannels");

            migrationBuilder.DropTable(
                name: "ignoredlogchannels");

            migrationBuilder.DropTable(
                name: "imageonlychannels");

            migrationBuilder.DropTable(
                name: "musicplayersettings");

            migrationBuilder.DropTable(
                name: "muteduserid");

            migrationBuilder.DropTable(
                name: "ncpixel");

            migrationBuilder.DropTable(
                name: "notify");

            migrationBuilder.DropTable(
                name: "patrons");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "plantedcurrency");

            migrationBuilder.DropTable(
                name: "playlistsong");

            migrationBuilder.DropTable(
                name: "quotes");

            migrationBuilder.DropTable(
                name: "rakeback");

            migrationBuilder.DropTable(
                name: "reactionroles");

            migrationBuilder.DropTable(
                name: "reminders");

            migrationBuilder.DropTable(
                name: "repeaters");

            migrationBuilder.DropTable(
                name: "rewardedusers");

            migrationBuilder.DropTable(
                name: "rotatingstatus");

            migrationBuilder.DropTable(
                name: "sar");

            migrationBuilder.DropTable(
                name: "sarautodelete");

            migrationBuilder.DropTable(
                name: "shopentryitem");

            migrationBuilder.DropTable(
                name: "slowmodeignoredrole");

            migrationBuilder.DropTable(
                name: "slowmodeignoreduser");

            migrationBuilder.DropTable(
                name: "stickyroles");

            migrationBuilder.DropTable(
                name: "streamonlinemessages");

            migrationBuilder.DropTable(
                name: "streamroleblacklisteduser");

            migrationBuilder.DropTable(
                name: "streamrolewhitelisteduser");

            migrationBuilder.DropTable(
                name: "temprole");

            migrationBuilder.DropTable(
                name: "todos");

            migrationBuilder.DropTable(
                name: "unbantimer");

            migrationBuilder.DropTable(
                name: "unmutetimer");

            migrationBuilder.DropTable(
                name: "unroletimer");

            migrationBuilder.DropTable(
                name: "userbetstats");

            migrationBuilder.DropTable(
                name: "userfishstats");

            migrationBuilder.DropTable(
                name: "userxpstats");

            migrationBuilder.DropTable(
                name: "vcroleinfo");

            migrationBuilder.DropTable(
                name: "waifuitem");

            migrationBuilder.DropTable(
                name: "waifuupdates");

            migrationBuilder.DropTable(
                name: "warningpunishment");

            migrationBuilder.DropTable(
                name: "warnings");

            migrationBuilder.DropTable(
                name: "xpcurrencyreward");

            migrationBuilder.DropTable(
                name: "xprolereward");

            migrationBuilder.DropTable(
                name: "xpshopowneditem");

            migrationBuilder.DropTable(
                name: "antispamsetting");

            migrationBuilder.DropTable(
                name: "autotranslatechannels");

            migrationBuilder.DropTable(
                name: "guildfilterconfig");

            migrationBuilder.DropTable(
                name: "giveawaymodel");

            migrationBuilder.DropTable(
                name: "logsettings");

            migrationBuilder.DropTable(
                name: "guildconfigs");

            migrationBuilder.DropTable(
                name: "musicplaylists");

            migrationBuilder.DropTable(
                name: "sargroup");

            migrationBuilder.DropTable(
                name: "shopentry");

            migrationBuilder.DropTable(
                name: "streamrolesettings");

            migrationBuilder.DropTable(
                name: "todosarchive");

            migrationBuilder.DropTable(
                name: "waifuinfo");

            migrationBuilder.DropTable(
                name: "xpsettings");

            migrationBuilder.DropTable(
                name: "clubs");

            migrationBuilder.DropTable(
                name: "discorduser");
        }
    }
}
