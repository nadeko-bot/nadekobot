using CommandLine;
using Nadeko.Common.Medusa;

namespace NadekoBot.Common;

public sealed class CommandsUtilityService : ICommandsUtilityService, INService
{
    private readonly CommandHandler _ch;
    private readonly IBotStrings _strings;
    private readonly DiscordPermOverrideService _dpos;
    private readonly IMessageSenderService _sender;
    private readonly ILocalization _loc;
    private readonly IMedusaLoaderService _medusae;

    public CommandsUtilityService(
        CommandHandler ch,
        IBotStrings strings,
        DiscordPermOverrideService dpos,
        IMessageSenderService sender,
        ILocalization loc,
        IMedusaLoaderService medusae)
    {
        _ch = ch;
        _strings = strings;
        _dpos = dpos;
        _sender = sender;
        _loc = loc;
        _medusae = medusae;
    }

    public EmbedBuilder GetCommandHelp(CommandInfo com, IGuild guild)
    {
        var prefix = _ch.GetPrefix(guild);

        var str = $"**`{prefix + com.Aliases.First()}`**";
        var alias = com.Aliases.Skip(1).FirstOrDefault();
        if (alias is not null)
            str += $" **| `{prefix + alias}`**";

        var culture = _loc.GetCultureInfo(guild);

        var em = _sender.CreateEmbed(guild?.Id)
                        .AddField(str, $"{com.RealSummary(_strings, _medusae, culture, prefix)}", true);

        _dpos.TryGetOverrides(guild?.Id ?? 0, com.Name, out var overrides);
        var reqs = GetCommandRequirements(com, (GuildPermission?)overrides);
        if (reqs.Any())
            em.AddField(GetText(strs.requires, guild), string.Join("\n", reqs));

        var paramList = _strings.GetCommandStrings(com.Name, culture)?.Params;
        em
            .WithOkColor()
            .AddField(_strings.GetText(strs.usage),
                string.Join("\n", com.RealRemarksArr(_strings, _medusae, culture, prefix).Map(arg => Format.Code(arg))))
            .WithFooter(GetText(strs.module(com.Module.GetTopLevelModule().Name), guild));

        if (paramList is not null and not [])
        {
            var pl = paramList
                     .Select(x => Format.Code($"{prefix}{com.Name} {x.Keys.Select(y => $"<{y}>").Join(' ')}"))
                     .Join('\n');

            em.AddField(GetText(strs.overloads, guild), pl);
        }

        var opt = GetNadekoOptionType(com.Attributes);
        if (opt is not null)
        {
            var hs = GetCommandOptionHelp(opt);
            if (!string.IsNullOrWhiteSpace(hs))
                em.AddField(GetText(strs.options, guild), hs);
        }

        return em;
    }

    public static string GetCommandOptionHelp(Type opt)
    {
        var strs = GetCommandOptionHelpList(opt);

        return string.Join("\n", strs);
    }

    public static List<string> GetCommandOptionHelpList(Type opt)
    {
        var strs = opt.GetProperties()
                      .Select(x => x.GetCustomAttributes(true).FirstOrDefault(a => a is OptionAttribute))
                      .Where(x => x is not null)
                      .Cast<OptionAttribute>()
                      .Select(x =>
                      {
                          var toReturn = $"`--{x.LongName}`";

                          if (!string.IsNullOrWhiteSpace(x.ShortName))
                              toReturn += $" (`-{x.ShortName}`)";

                          toReturn += $"   {x.HelpText}  ";
                          return toReturn;
                      })
                      .ToList();

        return strs;
    }

    public static Type? GetNadekoOptionType(IEnumerable<Attribute> attributes)
        => attributes
           .Select(a => a.GetType())
           .Where(a => a.IsGenericType
                       && a.GetGenericTypeDefinition() == typeof(NadekoOptionsAttribute<>))
           .Select(a => a.GenericTypeArguments[0])
           .FirstOrDefault();

    public static string[] GetCommandRequirements(CommandInfo cmd, GuildPerm? overrides = null)
    {
        var toReturn = new List<string>();

        if (cmd.Preconditions.Any(x => x is OwnerOnlyAttribute))
            toReturn.Add("Bot Owner Only");

        if (cmd.Preconditions.Any(x => x is NoPublicBotAttribute)
            || cmd.Module
                  .Preconditions
                  .Any(x => x is NoPublicBotAttribute)
            || cmd.Module.GetTopLevelModule()
                  .Preconditions
                  .Any(x => x is NoPublicBotAttribute))
            toReturn.Add("No Public Bot");

        if (cmd.Preconditions
               .Any(x => x is OnlyPublicBotAttribute)
            || cmd.Module
                  .Preconditions
                  .Any(x => x is OnlyPublicBotAttribute)
            || cmd.Module.GetTopLevelModule()
                  .Preconditions
                  .Any(x => x is OnlyPublicBotAttribute))
            toReturn.Add("Only Public Bot");

        var userPermString = cmd.Preconditions
                                .Where(ca => ca is UserPermAttribute)
                                .Cast<UserPermAttribute>()
                                .Select(userPerm =>
                                {
                                    if (userPerm.ChannelPermission is { } cPerm)
                                        return GetPreconditionString(cPerm);

                                    if (userPerm.GuildPermission is { } gPerm)
                                        return GetPreconditionString(gPerm);

                                    return string.Empty;
                                })
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Join('\n');

        if (overrides is null)
        {
            if (!string.IsNullOrWhiteSpace(userPermString))
                toReturn.Add(userPermString);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(userPermString))
                toReturn.Add(Format.Strikethrough(userPermString));

            toReturn.Add(GetPreconditionString(overrides.Value));
        }

        return toReturn.ToArray();
    }

    public static string GetPreconditionString(ChannelPerm perm)
        => (perm + " Channel Permission").Replace("Guild", "Server");

    public static string GetPreconditionString(GuildPerm perm)
        => (perm + " Server Permission").Replace("Guild", "Server");

    public string GetText(LocStr str, IGuild? guild)
        => _strings.GetText(str, guild?.Id);
}

public interface ICommandsUtilityService
{
    EmbedBuilder GetCommandHelp(CommandInfo com, IGuild guild);
}