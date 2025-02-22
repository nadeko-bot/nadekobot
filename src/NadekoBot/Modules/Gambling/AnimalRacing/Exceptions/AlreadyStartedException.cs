#nullable disable
namespace NadekoBot.Modules.Gambling.Common.AnimalRacing.Exceptions;

public class AlreadyStartedException : Exception
{
    public AlreadyStartedException()
    {
    }

    public AlreadyStartedException(string message)
        : base(message)
    {
    }

    public AlreadyStartedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}