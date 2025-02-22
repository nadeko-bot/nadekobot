#nullable disable
namespace NadekoBot.Modules.Searches;

public interface ITranslateService
{
    public Task<string> Translate(string source, string target, string text = null);
    Task<bool> ToggleAtl(ulong guildId, ulong channelId, bool autoDelete);
    IEnumerable<string> GetLanguages();

    Task<bool?> RegisterUserAsync(
        ulong userId,
        ulong channelId,
        string from,
        string to);

    Task<bool> UnregisterUser(ulong channelId, ulong userId);
}