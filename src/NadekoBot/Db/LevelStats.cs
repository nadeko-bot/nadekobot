#nullable disable

namespace NadekoBot.Db;

public readonly struct LevelStats
{
    public long Level { get; }
    public long LevelXp { get; }
    public long RequiredXp { get; }
    public long TotalXp { get; }

    public LevelStats(long totalXp)
    {
        if (totalXp < 0)
            totalXp = 0;

        TotalXp = totalXp;
        Level = GetLevelByTotalXp(totalXp);
        LevelXp = totalXp - GetTotalXpReqForLevel(Level);
        RequiredXp = (9 * (Level + 1)) + 27;
    }

    public static LevelStats CreateForLevel(long level)
        => new(GetTotalXpReqForLevel(level));

    public static long GetTotalXpReqForLevel(long level)
        => ((9 * level * level) + (63 * level)) / 2;

    public static long GetLevelByTotalXp(long totalXp)
        => (long)((-7.0 / 2) + (1 / 6.0 * Math.Sqrt((8 * totalXp) + 441)));
}