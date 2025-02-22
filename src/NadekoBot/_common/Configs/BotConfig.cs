#nullable disable

using Cloneable;
using NadekoBot.Common.Yml;
using SixLabors.ImageSharp.PixelFormats;
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace NadekoBot.Common.Configs;

[Cloneable]
public sealed partial class BotConfig : ICloneable<BotConfig>
{
    [Comment("""DO NOT CHANGE""")]
    public int Version { get; set; } = 9;

    [Comment("""
        Most commands, when executed, have a small colored line
        next to the response. The color depends whether the command
        is completed, errored or in progress (pending)
        Color settings below are for the color of those lines.
        To get color's hex, you can go here https://htmlcolorcodes.com/
        and copy the hex code fo your selected color (marked as #)
        """)]
    public ColorConfig Color { get; set; }

    [Comment("Default bot language. It has to be in the list of supported languages (.langli)")]
    public CultureInfo DefaultLocale { get; set; }

    [Comment("""
        Style in which executed commands will show up in the logs.
        Allowed values: Simple, Normal, None
        """)]
    public ConsoleOutputType ConsoleOutputType { get; set; }

    [Comment("""Whether the bot will check for new releases every hour""")]
    public bool CheckForUpdates { get; set; } = true;

    [Comment("""Do you want any messages sent by users in Bot's DM to be forwarded to the owner(s)?""")]
    public bool ForwardMessages { get; set; }

    [Comment("""
            Do you want the message to be forwarded only to the first owner specified in the list of owners (in creds.yml),
            or all owners? (this might cause the bot to lag if there's a lot of owners specified)
            """)]
    public bool ForwardToAllOwners { get; set; }
    
    [Comment("""
        Any messages sent by users in Bot's DM to be forwarded to the specified channel.
        This option will only work when ForwardToAllOwners is set to false
        """)]
    public ulong? ForwardToChannel { get; set; }
    
    [Comment("""
             Should the bot ignore messages from other bots?
             Settings this to false might get your bot banned if it gets into a spam loop with another bot.
             This will only affect command executions, other features will still block bots from access.
             Default true
             """)]
    public bool IgnoreOtherBots { get; set; }

    [Comment("""
        When a user DMs the bot with a message which is not a command
        they will receive this message. Leave empty for no response. The string which will be sent whenever someone DMs the bot.
        Supports embeds. How it looks: https://puu.sh/B0BLV.png
        """)]
    [YamlMember(ScalarStyle = ScalarStyle.Literal)]
    public string DmHelpText { get; set; }

    [Comment("""
        Only users who send a DM to the bot containing one of the specified words will get a DmHelpText response.
        Case insensitive.
        Leave empty to reply with DmHelpText to every DM.
        """)]
    public List<string> DmHelpTextKeywords { get; set; }

    [Comment("""This is the response for the .h command""")]
    [YamlMember(ScalarStyle = ScalarStyle.Literal)]
    public string HelpText { get; set; }

    [Comment("""List of modules and commands completely blocked on the bot""")]
    public BlockedConfig Blocked { get; set; }

    [Comment("""List of modules and commands blocked from usage in DMs on the bot""")]
    public BlockedConfig DmBlocked { get; set; } = new();

    [Comment("""Which string will be used to recognize the commands""")]
    public string Prefix { get; set; }

    [Comment("""
        Whether the bot will rotate through all specified statuses.
        This setting can be changed via .ropl command.
        See RotatingStatuses submodule in Administration.
        """)]
    public bool RotateStatuses { get; set; }

    public BotConfig()
    {
        var color = new ColorConfig();
        Color = color;
        DefaultLocale = new("en-US");
        ConsoleOutputType = ConsoleOutputType.Normal;
        ForwardMessages = false;
        ForwardToAllOwners = false;
        DmHelpText = """{"description": "Type `%prefix%h` for help."}""";
        HelpText = """
            {
              "title": "To invite me to your server, use this link",
              "description": "https://discordapp.com/oauth2/authorize?client_id={0}&scope=bot&permissions=66186303",
              "color": 53380,
              "thumbnail": "https://i.imgur.com/nKYyqMK.png",
              "fields": [
                {
                  "name": "Useful help commands",
                  "value": "`%bot.prefix%modules` Lists all bot modules.
            `%prefix%h CommandName` Shows some help about a specific command.
            `%prefix%commands ModuleName` Lists all commands in a module.",
                  "inline": false
                },
                {
                  "name": "List of all Commands",
                  "value": "https://nadeko.bot/commands",
                  "inline": false
                },
                {
                  "name": "Nadeko Support Server",
                  "value": "https://discord.nadeko.bot/ ",
                  "inline": true
                }
              ]
            }
            """;
        var blocked = new BlockedConfig();
        Blocked = blocked;
        Prefix = ".";
        RotateStatuses = false;
        DmHelpTextKeywords =
        [
            "help",
            "commands",
            "cmds",
            "module",
            "can you do"
        ];
    }

//         [Comment(@"Whether the prefix will be a suffix, or prefix.
// For example, if your prefix is ! you will run a command called 'cash' by typing either
// '!cash @Someone' if your prefixIsSuffix: false or
// 'cash @Someone!' if your prefixIsSuffix: true")]
//         public bool PrefixIsSuffix { get; set; }

    // public string Prefixed(string text) => PrefixIsSuffix
    //     ? text + Prefix
    //     : Prefix + text;

    public string Prefixed(string text)
        => Prefix + text;
}

[Cloneable]
public sealed partial class BlockedConfig
{
    public HashSet<string> Commands { get; set; }
    public HashSet<string> Modules { get; set; }

    public BlockedConfig()
    {
        Modules = [];
        Commands = [];
    }
}

[Cloneable]
public partial class ColorConfig
{
    [Comment("""Color used for embed responses when command successfully executes""")]
    public Rgba32 Ok { get; set; }

    [Comment("""Color used for embed responses when command has an error""")]
    public Rgba32 Error { get; set; }

    [Comment("""Color used for embed responses while command is doing work or is in progress""")]
    public Rgba32 Pending { get; set; }

    public ColorConfig()
    {
        Ok = Rgba32.ParseHex("00e584");
        Error = Rgba32.ParseHex("ee281f");
        Pending = Rgba32.ParseHex("faa61a");
    }
}

public enum ConsoleOutputType
{
    Normal = 0,
    Simple = 1,
    None = 2
}
