#nullable disable
namespace NadekoBot.Modules.Gambling.Common.AnimalRacing.Exceptions;

public class AlreadyJoinedException : Exception
{
    public AlreadyJoinedException()
    {
    }

    public AlreadyJoinedException(string message)
        : base(message)
    {
    }

    public AlreadyJoinedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}