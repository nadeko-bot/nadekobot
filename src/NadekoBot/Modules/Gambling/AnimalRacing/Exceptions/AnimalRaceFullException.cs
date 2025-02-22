#nullable disable
namespace NadekoBot.Modules.Gambling.Common.AnimalRacing.Exceptions;

public class AnimalRaceFullException : Exception
{
    public AnimalRaceFullException()
    {
    }

    public AnimalRaceFullException(string message)
        : base(message)
    {
    }

    public AnimalRaceFullException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}