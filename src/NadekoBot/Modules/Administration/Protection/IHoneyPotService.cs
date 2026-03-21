namespace NadekoBot.Modules.Administration.Honeypot;

public interface IHoneyPotService
{
    public Task<bool> ToggleHoneypotChannel(ulong guildId, ulong channelId);
}