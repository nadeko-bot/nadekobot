#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Xp;

public class FullUserStats
{
    public DiscordUser User { get; }
    public UserXpStats FullGuildStats { get; }
    public LevelStats Global { get; }
    public LevelStats Guild { get; }
    public int GlobalRanking { get; }
    public int GuildRanking { get; }

    public FullUserStats(
        DiscordUser usr,
        UserXpStats fullGuildStats,
        LevelStats global,
        LevelStats guild,
        int globalRanking,
        int guildRanking)
    {
        User = usr;
        Global = global;
        Guild = guild;
        GlobalRanking = globalRanking;
        GuildRanking = guildRanking;
        FullGuildStats = fullGuildStats;
    }
}