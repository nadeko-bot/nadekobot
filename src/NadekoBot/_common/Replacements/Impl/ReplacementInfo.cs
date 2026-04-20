using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace NadekoBot.Common;

[Flags]
public enum ContextMask : byte
{
    None = 0,
    Client = 1,
    Guild = 2,
    Channel = 4,
    User = 8,
}

public static class ContextSlot
{
    public const int Client = 0;
    public const int Guild = 1;
    public const int Channel = 2;
    public const int User = 3;
    public const int Count = 4;

    public static int Classify(Type t)
    {
        if (typeof(IDiscordClient).IsAssignableFrom(t))
            return Client;
        if (typeof(IGuild).IsAssignableFrom(t))
            return Guild;
        if (typeof(IMessageChannel).IsAssignableFrom(t))
            return Channel;
        if (typeof(IUser).IsAssignableFrom(t))
            return User;

        return -1;
    }

    public static ContextMask SlotToMask(int slot)
        => (ContextMask)(1 << slot);
}

public sealed class ReplacementInfo
{
    private readonly Func<object?[], ValueTask<string?>> _typedInvoker;
    public IReadOnlyCollection<Type> InputTypes { get; }
    public string Token { get; }
    public ContextMask RequiredMask { get; }
    public int[] ParamSlotIndices { get; }

    public ReplacementInfo(string token, Delegate del)
    {
        var paramTypes = del.GetMethodInfo().GetParameters().Select(static x => x.ParameterType).ToArray();
        InputTypes = paramTypes.AsReadOnly();
        Token = token;

        var mask = ContextMask.None;
        var slots = new int[paramTypes.Length];
        for (var i = 0; i < paramTypes.Length; i++)
        {
            var slot = ContextSlot.Classify(paramTypes[i]);
            slots[i] = slot;
            if (slot >= 0)
                mask |= ContextSlot.SlotToMask(slot);
        }

        RequiredMask = mask;
        ParamSlotIndices = slots;
        _typedInvoker = BuildTypedInvoker(del, paramTypes, slots);
    }

    private static Func<object?[], ValueTask<string?>> BuildTypedInvoker(
        Delegate del,
        Type[] paramTypes,
        int[] slots)
        => paramTypes.Length switch
        {
            0 => BuildZeroArgInvoker(del),
            1 => BuildOneArgInvoker(del, paramTypes[0], slots[0]),
            _ => BuildFallbackInvoker(del, slots)
        };

    private static Func<object?[], ValueTask<string?>> BuildZeroArgInvoker(Delegate del)
    {
        var typed = (Func<ValueTask<string>>)del;
        return _ =>
        {
            var result = typed();
            return Unsafe.As<ValueTask<string>, ValueTask<string?>>(ref result);
        };
    }

    private static Func<object?[], ValueTask<string?>> BuildOneArgInvoker(
        Delegate del,
        Type paramType,
        int slot)
    {
        if (typeof(IDiscordClient).IsAssignableFrom(paramType))
        {
            var typed = (Func<DiscordSocketClient, ValueTask<string>>)del;
            return inputData =>
            {
                var result = typed((DiscordSocketClient)inputData[slot]!);
                return Unsafe.As<ValueTask<string>, ValueTask<string?>>(ref result);
            };
        }

        if (typeof(IGuild).IsAssignableFrom(paramType))
        {
            var typed = (Func<IGuild, ValueTask<string>>)del;
            return inputData =>
            {
                var result = typed((IGuild)inputData[slot]!);
                return Unsafe.As<ValueTask<string>, ValueTask<string?>>(ref result);
            };
        }

        if (typeof(IMessageChannel).IsAssignableFrom(paramType))
        {
            var typed = (Func<IMessageChannel, ValueTask<string>>)del;
            return inputData =>
            {
                var result = typed((IMessageChannel)inputData[slot]!);
                return Unsafe.As<ValueTask<string>, ValueTask<string?>>(ref result);
            };
        }

        if (paramType.IsArray && typeof(IUser).IsAssignableFrom(paramType.GetElementType()!))
        {
            var typed = (Func<IUser[], ValueTask<string>>)del;
            return inputData =>
            {
                var result = typed((IUser[])inputData[slot]!);
                return Unsafe.As<ValueTask<string>, ValueTask<string?>>(ref result);
            };
        }

        // IGuildUser before IUser — Func<IGuildUser,R> is not castable to Func<IUser,R>
        if (typeof(IGuildUser).IsAssignableFrom(paramType))
        {
            var typed = (Func<IGuildUser, ValueTask<string>>)del;
            return inputData =>
            {
                var result = typed((IGuildUser)inputData[slot]!);
                return Unsafe.As<ValueTask<string>, ValueTask<string?>>(ref result);
            };
        }

        if (typeof(IUser).IsAssignableFrom(paramType))
        {
            var typed = (Func<IUser, ValueTask<string>>)del;
            return inputData =>
            {
                var result = typed((IUser)inputData[slot]!);
                return Unsafe.As<ValueTask<string>, ValueTask<string?>>(ref result);
            };
        }

        return BuildFallbackInvoker(del, new[] { slot });
    }

    private static Func<object?[], ValueTask<string?>> BuildFallbackInvoker(Delegate del, int[] slots)
    {
        return inputData =>
        {
            var objs = new object?[slots.Length];
            for (var i = 0; i < slots.Length; i++)
                objs[i] = slots[i] >= 0 ? inputData[slots[i]] : null;

            var result = del.DynamicInvoke(objs);
            if (result is ValueTask<string> vt)
                return Unsafe.As<ValueTask<string>, ValueTask<string?>>(ref vt);
            return default;
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<string?> GetValueAsync(object?[] inputData)
        => _typedInvoker(inputData);

    public override int GetHashCode()
        => Token.GetHashCode();

    public override bool Equals(object? obj)
        => obj is ReplacementInfo ri && ri.Token == Token;
}

public sealed class RegexReplacementInfo
{
    private readonly Func<object?[], Match, ValueTask<string?>> _typedInvoker;
    public IReadOnlyCollection<Type> InputTypes { get; }
    public ContextMask RequiredMask { get; }
    public int[] ParamSlotIndices { get; }

    public Regex Regex { get; }
    public string Pattern { get; }

    private static readonly Func<Match, ValueTask<string?>> _fallbackFunc = static _ => default;

    public RegexReplacementInfo(Regex regex, Delegate del)
    {
        var paramTypes = del.GetMethodInfo().GetParameters().Select(static x => x.ParameterType).ToArray();
        InputTypes = paramTypes.AsReadOnly();
        Regex = regex;
        Pattern = Regex.ToString();

        var mask = ContextMask.None;
        var slots = new int[paramTypes.Length];
        for (var i = 0; i < paramTypes.Length; i++)
        {
            var slot = ContextSlot.Classify(paramTypes[i]);
            slots[i] = slot;
            if (slot >= 0)
                mask |= ContextSlot.SlotToMask(slot);
        }

        RequiredMask = mask;
        ParamSlotIndices = slots;
        _typedInvoker = BuildTypedInvoker(del, paramTypes, slots);
    }

    private static Func<object?[], Match, ValueTask<string?>> BuildTypedInvoker(
        Delegate del,
        Type[] paramTypes,
        int[] slots)
    {
        if (paramTypes.Length == 0)
        {
            var typed = (Func<Func<Match, ValueTask<string>>>)del;
            return (_, m) =>
            {
                var inner = typed();
                var result = inner(m);
                return Unsafe.As<ValueTask<string>, ValueTask<string?>>(ref result);
            };
        }

        return (inputData, m) =>
        {
            var objs = new object?[slots.Length];
            for (var i = 0; i < slots.Length; i++)
                objs[i] = slots[i] >= 0 ? inputData[slots[i]] : null;

            var inner = del.DynamicInvoke(objs) as Func<Match, ValueTask<string?>> ?? _fallbackFunc;
            return inner(m);
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<string?> GetValueAsync(Match m, object?[] inputData)
        => _typedInvoker(inputData, m);

    public override int GetHashCode()
        => Regex.GetHashCode();

    public override bool Equals(object? obj)
        => obj is RegexReplacementInfo ri && ri.Pattern == Pattern;
}