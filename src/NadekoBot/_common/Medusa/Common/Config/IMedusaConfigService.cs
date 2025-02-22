namespace NadekoBot.Medusa;

public interface IMedusaConfigService
{
    IReadOnlyCollection<string> GetLoadedMedusae();
    void AddLoadedMedusa(string name);
    void RemoveLoadedMedusa(string name);
}