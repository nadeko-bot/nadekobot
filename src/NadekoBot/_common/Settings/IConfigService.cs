namespace NadekoBot.Services;

public interface IConfigService
{
    public string Name { get; }

    void Reload();

    IReadOnlyList<string> GetSettableProps();

    string? GetSetting(string prop);

    string? GetComment(string prop);

    bool SetSetting(string prop, string newValue);
}