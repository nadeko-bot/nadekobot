using System.Text.RegularExpressions;

namespace NadekoBot.Common;

public sealed partial class ReplacementPatternStore
{
    private static readonly Regex _rngRegex = new(@"%rng(?:(?<from>(?:-)?\d+)-(?<to>(?:-)?\d+))?%",
        RegexOptions.Compiled);


    private void WithDefault()
    {
        Register("%bot.time%",
            static ()
                => TimestampTag.FromDateTime(DateTime.UtcNow, TimestampTagStyles.ShortTime).ToString());

        Register("%bot.date%",
            static ()
                => TimestampTag.FromDateTime(DateTime.UtcNow, TimestampTagStyles.ShortDate).ToString());
    }

    private void WithClient()
    {
        Register("%bot.status%", static (DiscordSocketClient client) => client.Status.ToString());
        Register("%bot.latency%", static (DiscordSocketClient client) => client.Latency.ToString());
        Register("%bot.name%", static (DiscordSocketClient client) => client.CurrentUser.Username);
        Register("%bot.fullname%", static (DiscordSocketClient client) => client.CurrentUser.ToString());
        Register("%bot.discrim%", static (DiscordSocketClient client) => client.CurrentUser.Discriminator);
        Register("%bot.id%", static (DiscordSocketClient client) => client.CurrentUser.Id.ToString());
        Register("%bot.avatar%",
            static (DiscordSocketClient client) => client.CurrentUser.RealAvatarUrl().ToString());

        Register("%bot.mention%", static (DiscordSocketClient client) => client.CurrentUser.Mention);

        Register("%shard.servercount%", static (DiscordSocketClient c) => c.Guilds.Count.ToString());
        Register("%shard.usercount%",
            static (DiscordSocketClient c) => c.Guilds.Sum(g => g.MemberCount).ToString());
        Register("%shard.id%", static (DiscordSocketClient c) => c.ShardId.ToString());
    }

    private void WithServer()
    {
        Register("%server%", static (IGuild g) => g.Name);
        Register("%server.id%", static (IGuild g) => g.Id.ToString());
        Register("%server.name%", static (IGuild g) => g.Name);
        Register("%server.icon%", static (IGuild g) => g.IconUrl);
        Register("%server.members%", static (IGuild g) => (g as SocketGuild)?.MemberCount.ToString() ?? "?");
        Register("%server.boosters%", static (IGuild g) => g.PremiumSubscriptionCount.ToString());
        Register("%server.boost_level%", static (IGuild g) => ((int)g.PremiumTier).ToString());
    }

    private void WithChannel()
    {
        Register("%channel%", static (IMessageChannel ch) => ch.Name);
        Register("%channel.mention%",
            static (IMessageChannel ch) => (ch as ITextChannel)?.Mention ?? "#" + ch.Name);
        Register("%channel.name%", static (IMessageChannel ch) => ch.Name);
        Register("%channel.id%", static (IMessageChannel ch) => ch.Id.ToString());
        Register("%channel.created%",
            static (IMessageChannel ch) => ch.CreatedAt.ToString("HH:mm dd.MM.yyyy"));
        Register("%channel.nsfw%",
            static (IMessageChannel ch) => (ch as ITextChannel)?.IsNsfw.ToString() ?? "-");
        Register("%channel.topic%", static (IMessageChannel ch) => (ch as ITextChannel)?.Topic ?? "-");
    }

    private void WithUsers()
    {
        Register("%user%", static (IUser user) => user.Mention);
        Register("%user.mention%", static (IUser user) => user.Mention);
        Register("%user.fullname%", static (IUser user) => user.ToString()!);
        Register("%user.name%", static (IUser user) => user.Username);
        Register("%user.displayname%", static (IUser user) => user is IGuildUser gu ? gu.DisplayName : user.Username);
        Register("%user.discrim%", static (IUser user) => user.Discriminator);
        Register("%user.avatar%", static (IUser user) => user.RealAvatarUrl().ToString());
        Register("%user.id%", static (IUser user) => user.Id.ToString());
        Register("%user.created_time%", static (IUser user) => user.CreatedAt.ToString("HH:mm"));
        Register("%user.created_date%", static (IUser user) => user.CreatedAt.ToString("dd.MM.yyyy"));
        Register("%user.joined_time%", static (IGuildUser user) => user.JoinedAt?.ToString("HH:mm") ?? "??:??");
        Register("%user.joined_date%",
            static (IGuildUser user) => user.JoinedAt?.ToString("dd.MM.yyyy") ?? "??.??.????");

        Register("%user%",
            static (IUser[] users) => string.Join(" ", users.Select(user => user.Mention)));
        Register("%user.mention%",
            static (IUser[] users) => string.Join(" ", users.Select(user => user.Mention)));
        Register("%user.fullname%",
            static (IUser[] users) => string.Join(" ", users.Select(user => user.ToString())));
        Register("%user.name%",
            static (IUser[] users) => string.Join(" ", users.Select(user => user.Username)));
        Register("%user.discrim%",
            static (IUser[] users) => string.Join(" ", users.Select(user => user.Discriminator)));
        Register("%user.avatar%",
            static (IUser[] users)
                => string.Join(" ", users.Select(user => user.RealAvatarUrl().ToString())));
        Register("%user.id%",
            static (IUser[] users) => string.Join(" ", users.Select(user => user.Id.ToString())));
        Register("%user.created_time%",
            static (IUser[] users)
                => string.Join(" ", users.Select(user => user.CreatedAt.ToString("HH:mm"))));
        Register("%user.created_date%",
            static (IUser[] users)
                => string.Join(" ", users.Select(user => user.CreatedAt.ToString("dd.MM.yyyy"))));
        Register("%user.joined_time%",
            static (IUser[] users) => string.Join(" ",
                users.Select(user => (user as IGuildUser)?.JoinedAt?.ToString("HH:mm") ?? "-")));
        Register("%user.joined_date%",
            static (IUser[] users) => string.Join(" ",
                users.Select(user => (user as IGuildUser)?.JoinedAt?.ToString("dd.MM.yyyy") ?? "-")));
    }

    private void WithRegex()
    {
        Register(_rngRegex,
            match =>
            {
                var rng = new NadekoRandom();
                if (!int.TryParse(match.Groups["from"].ToString(), out var from))
                    from = 0;
                if (!int.TryParse(match.Groups["to"].ToString(), out var to))
                    to = 0;

                if (from == 0 && to == 0)
                    return rng.Next(0, 11).ToString();

                if (from >= to)
                    return string.Empty;

                return rng.Next(from, to + 1).ToString();
            });
    }
}