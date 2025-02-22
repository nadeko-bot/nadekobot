namespace NadekoBot.Common.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class NadekoOptionsAttribute<TOption> : Attribute
    where TOption: INadekoCommandOptions
{
}