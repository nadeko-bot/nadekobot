namespace NadekoBot.Modules.Xp;

public readonly record struct XpRate(XpRateType Type, long Amount, float Cooldown)
{
    public bool IsExcluded()
        => Amount == 0 || Cooldown == 0;
}