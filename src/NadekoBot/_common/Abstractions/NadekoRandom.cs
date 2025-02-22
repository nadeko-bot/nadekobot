#nullable disable
using System.Security.Cryptography;

namespace Nadeko.Common;

public sealed class NadekoRandom : Random
{
    private readonly RandomNumberGenerator _rng;

    public NadekoRandom()
        => _rng = RandomNumberGenerator.Create();

    public override int Next()
    {
        var bytes = new byte[sizeof(int)];
        _rng.GetBytes(bytes);
        return Math.Abs(BitConverter.ToInt32(bytes, 0));
    }

    /// <summary>
    /// Generates a random integer between 0 (inclusive) and
    /// a specified exclusive upper bound using a cryptographically strong random number generator.
    /// </summary>
    /// <param name="maxValue">Exclusive max value</param>
    /// <returns>A random number</returns>
    public override int Next(int maxValue)
        => RandomNumberGenerator.GetInt32(maxValue);

    /// <summary>
    /// Generates a random integer between a specified inclusive lower bound and a
    /// specified exclusive upper bound using a cryptographically strong random number generator.
    /// </summary>
    /// <param name="minValue">Inclusive min value</param>
    /// <param name="maxValue">Exclusive max value</param>
    /// <returns>A random number</returns>
    public override int Next(int minValue, int maxValue)
        => RandomNumberGenerator.GetInt32(minValue, maxValue);

    public long NextLong(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        if (minValue == maxValue)
            return minValue;
        var bytes = new byte[sizeof(long)];
        _rng.GetBytes(bytes);
        var sign = Math.Sign(BitConverter.ToInt64(bytes, 0));
        return (sign * BitConverter.ToInt64(bytes, 0) % (maxValue - minValue)) + minValue;
    }

    public override void NextBytes(byte[] buffer)
        => _rng.GetBytes(buffer);

    protected override double Sample()
    {
        var bytes = new byte[sizeof(double)];
        _rng.GetBytes(bytes);
        return Math.Abs((BitConverter.ToDouble(bytes, 0) / (double.MaxValue + 1)));
    }

    public override double NextDouble()
        => Sample();
}