using System.Runtime.CompilerServices;

namespace Nadeko.Common;

// needs proper invalid input check (character array input out of range)
// needs negative number support
// ReSharper disable once InconsistentNaming
#pragma warning disable CS8981
public readonly struct kwum : IEquatable<kwum>
#pragma warning restore CS8981
{
    private const string VALID_CHARACTERS = "23456789abcdefghijkmnpqrstuvwxyz";
    private readonly int _value;

    public kwum(int num)
        => _value = num;

    public kwum(in char c)
    {
        if (!IsValidChar(c))
            throw new ArgumentException("Character needs to be a valid kwum character.", nameof(c));

        _value = InternalCharToValue(c);
    }

    public kwum(in ReadOnlySpan<char> input)
    {
        _value = 0;
        for (var index = 0; index < input.Length; index++)
        {
            var c = input[index];
            if (!IsValidChar(c))
                throw new ArgumentException("All characters need to be a valid kwum characters.", nameof(input));

            _value += VALID_CHARACTERS.IndexOf(c) * (int)Math.Pow(VALID_CHARACTERS.Length, input.Length - index - 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int InternalCharToValue(in char c)
        => VALID_CHARACTERS.IndexOf(c);

    public static bool TryParse(in ReadOnlySpan<char> input, out kwum value)
    {
        value = default;
        foreach (var c in input)
        {
            if (!IsValidChar(c))
                return false;
        }

        value = new(input);
        return true;
    }

    public static kwum operator +(kwum left, kwum right)
        => new(left._value + right._value);

    public static bool operator ==(kwum left, kwum right)
        => left._value == right._value;

    public static bool operator !=(kwum left, kwum right)
        => !(left == right);

    public static implicit operator long(kwum kwum)
        => kwum._value;

    public static implicit operator int(kwum kwum)
        => kwum._value;

    public static implicit operator kwum(int num)
        => new(num);

    public static bool IsValidChar(char c)
        => VALID_CHARACTERS.Contains(c);

    public override string ToString()
    {
        if (_value == 0)
            return VALID_CHARACTERS[0].ToString();
        
        var count = VALID_CHARACTERS.Length;
        var localValue = _value;
        var arrSize = (int)Math.Log(localValue, count) + 1;
        Span<char> chars = new char[arrSize];
        while (localValue > 0)
        {
            localValue = Math.DivRem(localValue, count, out var rem);
            chars[--arrSize] = VALID_CHARACTERS[rem];
        }

        return new(chars);
    }

    public override bool Equals(object? obj)
        => obj is kwum kw && kw == this;

    public bool Equals(kwum other)
        => other == this;

    public override int GetHashCode()
        => _value.GetHashCode();
}