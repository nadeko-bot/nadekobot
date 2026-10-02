namespace NadekoBot.Modules.Utility.UnitConversion;

public enum UnitCategory : byte
{
    Length,
    Area,
    Volume,
    Mass,
    Time,
    Temperature,
    Speed,
    Acceleration,
    Pressure,
    Energy,
    Power,
    Force,
    Frequency,
    Data,
    DataRate,
    Angle,
    FuelEconomy,
    Current,
    Charge,
    Voltage,
    Resistance,
    Currency
}

[Flags]
public enum PrefixSet : byte
{
    None = 0,
    SiSmall = 1,
    SiLarge = 2,
    Binary = 4,
    Si = SiSmall | SiLarge,
    Data = SiLarge | Binary
}

public readonly record struct UnitPrefix(string Symbol, string Name, double Factor, PrefixSet Set);

public readonly record struct UnitDef(
    string Symbol,
    string Name,
    string Plural,
    UnitCategory Category,
    Dimension Dim,
    double Factor,
    PrefixSet Prefixes = PrefixSet.None,
    double Offset = 0,
    string[]? SymbolAliases = null,
    string[]? NameAliases = null);

public enum UnitKind : byte
{
    Compound,
    Unit,
    Currency
}

public readonly struct ResolvedUnit
{
    public readonly double Factor;
    public readonly double Offset;
    public readonly Dimension Dim;
    public readonly UnitKind Kind;
    public readonly short UnitIndex;
    public readonly sbyte PrefixIndex;
    public readonly string? CurrencyCode;
    public readonly bool IsRatio;

    public ResolvedUnit(
        double factor,
        double offset,
        in Dimension dim,
        UnitKind kind,
        short unitIndex = -1,
        sbyte prefixIndex = -1,
        string? currencyCode = null,
        bool isRatio = false)
    {
        Factor = factor;
        Offset = offset;
        Dim = dim;
        Kind = kind;
        UnitIndex = unitIndex;
        PrefixIndex = prefixIndex;
        CurrencyCode = currencyCode;
        IsRatio = isRatio;
    }

    public bool SameAs(in ResolvedUnit other)
        => Factor == other.Factor && Offset == other.Offset && Dim == other.Dim;
}

public enum ResolveStatus : byte
{
    Ok,
    Unknown,
    Invalid,
    RatesUnavailable
}
