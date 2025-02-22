#nullable disable
namespace NadekoBot.Modules.Searches.Common.Exceptions;

public class StreamNotFoundException : Exception
{
    public StreamNotFoundException()
    {
    }

    public StreamNotFoundException(string message)
        : base(message)
    {
    }

    public StreamNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}