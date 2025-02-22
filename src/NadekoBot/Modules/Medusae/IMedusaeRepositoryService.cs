namespace NadekoBot.Modules;

public interface IMedusaeRepositoryService
{
    Task<List<ModuleItem>> GetModuleItemsAsync();
}