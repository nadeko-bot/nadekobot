#nullable disable

namespace NadekoBot.Db;

public readonly struct LevelStats
{
    private const int DEFAULT_A = 9;
    private const int DEFAULT_C = 27;

    public long Level { get; }
    public long LevelXp { get; }
    public long RequiredXp { get; }
    public long TotalXp { get; }

    public LevelStats(long totalXp)
        : this(totalXp, DEFAULT_A, DEFAULT_C)
    {
    }

    public LevelStats(long totalXp, int a, int c)
    {
        if (totalXp < 0)
            totalXp = 0;

        TotalXp = totalXp;
        Level = GetLevelByTotalXp(totalXp, a, c);
        LevelXp = totalXp - GetTotalXpReqForLevel(Level, a, c);
        RequiredXp = (a * (Level + 1)) + c;
    }

    public static LevelStats CreateForLevel(long level)
        => new(GetTotalXpReqForLevel(level));

    public static LevelStats CreateForLevel(long level, int a, int c)
        => new(GetTotalXpReqForLevel(level, a, c), a, c);

    // T(n) = (a * n^2 + (2c + a) * n) / 2
    public static long GetTotalXpReqForLevel(long level)
        => GetTotalXpReqForLevel(level, DEFAULT_A, DEFAULT_C);

    public static long GetTotalXpReqForLevel(long level, int a, int c)
        => ((a * level * level) + ((2 * c + a) * level)) / 2;

    // Inverse via quadratic formula: a/2 * n^2 + (2c+a)/2 * n - totalXp = 0
    // n = ( -(2c+a) + sqrt((2c+a)^2 + 8a*totalXp) ) / (2a)
    public static long GetLevelByTotalXp(long totalXp)
        => GetLevelByTotalXp(totalXp, DEFAULT_A, DEFAULT_C);

    public static long GetLevelByTotalXp(long totalXp, int a, int c)
    {
        var d = (2.0 * c) + a;
        return (long)((-d + Math.Sqrt((d * d) + (8.0 * a * totalXp))) / (2.0 * a));
    }
}