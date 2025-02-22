#nullable disable
namespace NadekoBot.Modules.Gambling.Common;

public interface ICurrencyEvent
{
    event Func<ulong, Task> OnEnded;
    Task StopEvent();
    Task StartEvent();
}