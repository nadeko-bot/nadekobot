namespace NadekoBot.Common.Attributes;

// Marks a command which takes currency from the member who runs it. An alias may not give such a command fixed arguments.
[AttributeUsage(AttributeTargets.Method)]
public sealed class SpendsCurrencyAttribute : Attribute
{
    public static bool IsMarked(CommandInfo cmd)
    {
        foreach (var attr in cmd.Attributes)
        {
            if (attr is SpendsCurrencyAttribute)
                return true;
        }

        return false;
    }
}
