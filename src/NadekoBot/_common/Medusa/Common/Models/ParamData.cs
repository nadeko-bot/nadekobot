namespace NadekoBot.Medusa;

public sealed record ParamData(
    Type Type,
    string Name,
    bool IsOptional,
    object? DefaultValue,
    bool IsLeftover,
    bool IsParams
);