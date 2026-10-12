namespace NadekoBot.Services;

public enum ConfigPropType
{
    Text,
    Number,
    Decimal,
    Switch,
    Color,
    Choice,
    Language,
    Timezone
}

// Min and Max are inclusive. Only values set after startup are checked, so a loaded file may hold a value outside them.
public sealed record ConfigPropInfo(
    ConfigPropType Type,
    double? Min = null,
    double? Max = null,
    int? MaxLength = null,
    IReadOnlyList<string>? Options = null);
