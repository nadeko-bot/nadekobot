#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public enum ProtectionType
{
    Raiding,
    Spamming,
    Alting
}

public class AntiRaidStats
{
    public AntiRaidSetting AntiRaidSettings { get; set; }
    public int UsersCount { get; set; }
    public ConcurrentHashSet<IGuildUser> RaidUsers { get; set; } = new();
}

public class AntiSpamStats
{
    public AntiSpamSetting AntiSpamSettings { get; set; }
    public ConcurrentDictionary<ulong, UserSpamStats> UserStats { get; set; } = new();
}

public class AntiAltStats
{
    public PunishmentAction Action
        => _setting.Action;

    public int ActionDurationMinutes
        => _setting.ActionDurationMinutes;

    public ulong? RoleId
        => _setting.RoleId;

    public TimeSpan MinAge
        => _setting.MinAge;

    public int Counter
        => counter;

    private readonly AntiAltSetting _setting;

    private int counter;

    public AntiAltStats(AntiAltSetting setting)
        => _setting = setting;

    public void Increment()
        => Interlocked.Increment(ref counter);
}