using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nadeko.Common.Medusa;

namespace NadekoBot.Extensions;

public static class Extensions
{
    private static readonly Regex _urlRegex =
        new(@"^(https?|ftp)://(?<path>[^\s/$.?#].[^\s]*)$", RegexOptions.Compiled);

    /// <summary>
    ///     Converts <see cref="DateTime"/> to <see cref="DateOnly"/>
    /// </summary>
    /// <param name="dateTime"> The <see cref="DateTime"/> to convert. </param>
    /// <returns> The <see cref="DateOnly"/>. </returns>
    public static DateOnly ToDateOnly(this DateTime dateTime)
        => DateOnly.FromDateTime(dateTime);

    /// <summary>
    ///     Determines if <see cref="DateTime"/> is before today
    /// </summary>
    /// <param name="date"> The <see cref="DateTime"/> to check. </param>
    /// <returns> True if <see cref="DateTime"/> is before today. </returns>
    public static bool IsBeforeToday(this DateTime date)
        => date < DateTime.UtcNow.Date;

    public static Task EditAsync(this IUserMessage msg, SmartText text)
        => text switch
        {
            SmartEmbedText set => msg.ModifyAsync(x =>
            {
                x.Embed = set.IsValid ? set.GetEmbed().Build() : null;
                x.Content = set.PlainText?.SanitizeMentions() ?? "";
            }),
            SmartEmbedTextArray set => msg.ModifyAsync(x =>
            {
                x.Embeds = set.GetEmbedBuilders().Map(eb => eb.Build());
                x.Content = set.Content?.SanitizeMentions() ?? "";
            }),
            SmartPlainText spt => msg.ModifyAsync(x =>
            {
                x.Content = spt.Text.SanitizeMentions();
                x.Embed = null;
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(text))
        };

    public static ulong[] GetGuildIds(this DiscordSocketClient client)
        => client.Guilds
                 .Map(x => x.Id);

    /// <summary>
    ///     Generates a string in the format HHH:mm if timespan is &gt;= 2m.
    ///     Generates a string in the format 00:mm:ss if timespan is less than 2m.
    /// </summary>
    /// <param name="span">Timespan to convert to string</param>
    /// <returns>Formatted duration string</returns>
    public static string ToPrettyStringHm(this TimeSpan span)
    {
        if (span > TimeSpan.FromHours(24))
            return $"{span.Days:00}d:{span.Hours:00}h";

        if (span > TimeSpan.FromMinutes(2))
            return $"{span.Hours:00}h:{span.Minutes:00}m";

        return $"{span.Minutes:00}m:{span.Seconds:00}s";
    }

    public static double Megabytes(this int mb)
        => mb * 1024d * 1024;

    public static TimeSpan Hours(this int hours)
        => TimeSpan.FromHours(hours);

    public static TimeSpan Minutes(this int minutes)
        => TimeSpan.FromMinutes(minutes);

    public static TimeSpan Days(this int days)
        => TimeSpan.FromDays(days);

    public static TimeSpan Seconds(this int seconds)
        => TimeSpan.FromSeconds(seconds);

    public static bool TryGetUrlPath(this string input, out string path)
    {
        var match = _urlRegex.Match(input);
        if (match.Success)
        {
            path = match.Groups["path"].Value;
            return true;
        }

        path = string.Empty;
        return false;
    }

    public static IEmote ToIEmote(this string emojiStr)
        => Emote.TryParse(emojiStr, out var maybeEmote) ? maybeEmote : new Emoji(emojiStr);


    /// <summary>
    ///     First 10 characters of teh bot token.
    /// </summary>
    public static string RedisKey(this IBotCreds bc)
        => bc.Token[..10];

    public static bool IsAuthor(this IMessage msg, IDiscordClient client)
        => msg.Author?.Id == client.CurrentUser.Id;

    public static string RealSummary(
        this CommandInfo cmd,
        IBotStrings strings,
        IMedusaLoaderService medusae,
        CultureInfo culture,
        string prefix)
    {
        string description;
        if (cmd.Remarks?.StartsWith("medusa///") ?? false)
        {
            // command method name is kept in Summary
            // medusa///<medusa-name-here> is kept in remarks
            // this way I can find the name of the medusa, and then name of the command for which
            // the description should be loaded
            var medusaName = cmd.Remarks.Split("///")[1];
            description = medusae.GetCommandDescription(medusaName, cmd.Summary, culture);
        }
        else
        {
            description = strings.GetCommandStrings(cmd.Summary, culture).Desc;
        }

        return string.Format(description, prefix);
    }

    public static string[] RealRemarksArr(
        this CommandInfo cmd,
        IBotStrings strings,
        IMedusaLoaderService medusae,
        CultureInfo culture,
        string prefix)
    {
        string[] args;
        if (cmd.Remarks?.StartsWith("medusa///") ?? false)
        {
            // command method name is kept in Summary
            // medusa///<medusa-name-here> is kept in remarks
            // this way I can find the name of the medusa,
            // and command for which data should be loaded
            var medusaName = cmd.Remarks.Split("///")[1];
            args = medusae.GetCommandExampleArgs(medusaName, cmd.Summary, culture);
        }
        else
        {
            args = strings.GetCommandStrings(cmd.Summary, culture).Examples;
        }

        return args.Map(arg => GetFullUsage(cmd.Aliases.First(), arg, prefix));
    }

    private static string GetFullUsage(string commandName, string args, string prefix)
        => $"{prefix}{commandName} {string.Format(args, prefix)}".TrimEnd();

    public static EmbedBuilder AddPaginatedFooter(this EmbedBuilder embed, int curPage, int? lastPage)
    {
        if (lastPage is not null)
            return embed.WithFooter($"{curPage + 1} / {lastPage + 1}");

        return embed.WithFooter((curPage + 1).ToString());
    }

    // public static EmbedBuilder WithOkColor(this EmbedBuilder eb)
    //     => eb.WithColor(EmbedColor.Ok);
    //
    // public static EmbedBuilder WithPendingColor(this EmbedBuilder eb)
    //     => eb.WithColor(EmbedColor.Pending);
    //
    // public static EmbedBuilder WithErrorColor(this EmbedBuilder eb)
    //     => eb.WithColor(EmbedColor.Error);
    //
    public static IMessage DeleteAfter(this IUserMessage msg, float seconds, ILogCommandService? logService = null)
    {
        Task.Run(async () =>
        {
            await Task.Delay((int)(seconds * 1000));
            if (logService is not null)
                logService.AddDeleteIgnore(msg.Id);

            try
            {
                await msg.DeleteAsync();
            }
            catch
            {
            }
        });
        return msg;
    }

    public static ModuleInfo GetTopLevelModule(this ModuleInfo module)
    {
        while (module.Parent is not null)
            module = module.Parent;

        return module;
    }

    public static string GetGroupName(this ModuleInfo module)
        => module.Name.Replace("Commands", "", StringComparison.InvariantCulture);

    public static async Task<IEnumerable<IGuildUser>> GetMembersAsync(this IRole role)
    {
        var users = await role.Guild.GetUsersAsync(CacheMode.CacheOnly);
        return users.Where(u => u.RoleIds.Contains(role.Id));
    }

    public static string ToJson<T>(this T any, JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(any, options);

    public static Stream ToStream(this IEnumerable<byte> bytes, bool canWrite = false)
    {
        var ms = new MemoryStream(bytes as byte[] ?? bytes.ToArray(), canWrite);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    public static IEnumerable<IRole> GetRoles(this IGuildUser user)
        => user.RoleIds.Select(r => user.Guild.GetRole(r)).Where(r => r is not null);

    public static async Task<U> Fmap<T, U>(this Task<T> task, Func<T, U> func)
    {
        var res = await task;
        return func(res);
    }
}