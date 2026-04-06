using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration.Honeypot;

public interface IHoneyPotService
{
    public Task<bool> ToggleHoneypotChannel(ulong guildId, ulong channelId);
    public Task SetHoneypotChannel(ulong guildId, ulong channelId, HoneypotAction action);
}