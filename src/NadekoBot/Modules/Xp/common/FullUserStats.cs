#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Xp;

public class FullUserStats
{
    public DiscordUser User { get; }
    public UserXpStats FullGuildStats { get; }
    public LevelStats Guild { get; }
    public int GuildRanking { get; }

    public FullUserStats(
        DiscordUser usr,
        UserXpStats fullGuildStats,
        LevelStats guild,
        int guildRanking)
    {
        User = usr;
        Guild = guild;
        GuildRanking = guildRanking;
        FullGuildStats = fullGuildStats;
    }
}