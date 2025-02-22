namespace NadekoBot.Modules.Gambling;

public interface IGamblingCleanupService
{
    Task DeleteWaifus();
    Task DeleteWaifu(ulong userId);
    Task DeleteCurrency();
}