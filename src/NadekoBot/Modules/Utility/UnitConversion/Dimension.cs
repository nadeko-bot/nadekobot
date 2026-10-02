using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NadekoBot.Modules.Utility.UnitConversion;

public enum BaseDimension : byte
{
    Length,
    Mass,
    Time,
    Temperature,
    Current,
    Currency,
    Information,
    Angle
}

[InlineArray(Dimension.COUNT)]
public struct DimensionExponents
{
    private sbyte _e;
}

public readonly struct Dimension : IEquatable<Dimension>
{
    public const int COUNT = 8;

    private readonly DimensionExponents _exp;

    private Dimension(in DimensionExponents exp)
        => _exp = exp;

    public Dimension(
        sbyte length = 0,
        sbyte mass = 0,
        sbyte time = 0,
        sbyte temperature = 0,
        sbyte current = 0,
        sbyte currency = 0,
        sbyte information = 0,
        sbyte angle = 0)
    {
        var e = default(DimensionExponents);
        e[(int)BaseDimension.Length] = length;
        e[(int)BaseDimension.Mass] = mass;
        e[(int)BaseDimension.Time] = time;
        e[(int)BaseDimension.Temperature] = temperature;
        e[(int)BaseDimension.Current] = current;
        e[(int)BaseDimension.Currency] = currency;
        e[(int)BaseDimension.Information] = information;
        e[(int)BaseDimension.Angle] = angle;
        _exp = e;
    }

    public sbyte this[BaseDimension d]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _exp[(int)d];
    }

    public bool IsDimensionless
    {
        get
        {
            ReadOnlySpan<sbyte> s = _exp;
            return s.IndexOfAnyExcept((sbyte)0) < 0;
        }
    }

    // Combines this dimension with other^power. Fails instead of wrapping when an exponent leaves sbyte range.
    public bool TryCombine(in Dimension other, int power, out Dimension result)
    {
        ReadOnlySpan<sbyte> a = _exp;
        ReadOnlySpan<sbyte> b = other._exp;
        var e = default(DimensionExponents);
        for (var i = 0; i < COUNT; i++)
        {
            var v = a[i] + b[i] * power;
            if (v is > sbyte.MaxValue or < sbyte.MinValue)
            {
                result = default;
                return false;
            }

            e[i] = (sbyte)v;
        }

        result = new(e);
        return true;
    }

    public Dimension Inverse()
    {
        ReadOnlySpan<sbyte> a = _exp;
        var e = default(DimensionExponents);
        for (var i = 0; i < COUNT; i++)
            e[i] = (sbyte)-a[i];

        return new(e);
    }

    public bool Equals(Dimension other)
    {
        ReadOnlySpan<sbyte> a = _exp;
        return a.SequenceEqual((ReadOnlySpan<sbyte>)other._exp);
    }

    public override bool Equals(object? obj)
        => obj is Dimension d && Equals(d);

    public override int GetHashCode()
    {
        ReadOnlySpan<sbyte> a = _exp;
        return MemoryMarshal.Read<long>(MemoryMarshal.AsBytes(a)).GetHashCode();
    }

    public static bool operator ==(Dimension left, Dimension right)
        => left.Equals(right);

    public static bool operator !=(Dimension left, Dimension right)
        => !left.Equals(right);
}
