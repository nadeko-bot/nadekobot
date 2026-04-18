using System.Reflection;
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
    private readonly Delegate _del;
    public IReadOnlyCollection<Type> InputTypes { get; }
    public string Token { get; }
    public ContextMask RequiredMask { get; }
    public int[] ParamSlotIndices { get; }

    private static readonly Func<ValueTask<string?>> _falllbackFunc = static () => default;

    public ReplacementInfo(string token, Delegate del)
    {
        _del = del;
        var paramTypes = del.GetMethodInfo().GetParameters().Select(x => x.ParameterType).ToArray();
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
    }

    public async Task<string?> GetValueAsync(params object?[]? objs)
        => await (ValueTask<string?>)(_del.DynamicInvoke(objs) ?? _falllbackFunc);

    public override int GetHashCode()
        => Token.GetHashCode();

    public override bool Equals(object? obj)
        => obj is ReplacementInfo ri && ri.Token == Token;
}

public sealed class RegexReplacementInfo
{
    private readonly Delegate _del;
    public IReadOnlyCollection<Type> InputTypes { get; }
    public ContextMask RequiredMask { get; }
    public int[] ParamSlotIndices { get; }

    public Regex Regex { get; }
    public string Pattern { get; }

    private static readonly Func<Match, ValueTask<string?>> _falllbackFunc = static _ => default;

    public RegexReplacementInfo(Regex regex, Delegate del)
    {
        _del = del;
        var paramTypes = del.GetMethodInfo().GetParameters().Select(x => x.ParameterType).ToArray();
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
    }

    public async Task<string?> GetValueAsync(Match m, params object?[]? objs)
        => await ((Func<Match, ValueTask<string?>>)(_del.DynamicInvoke(objs) ?? _falllbackFunc))(m);

    public override int GetHashCode()
        => Regex.GetHashCode();

    public override bool Equals(object? obj)
        => obj is RegexReplacementInfo ri && ri.Pattern == Pattern;
}