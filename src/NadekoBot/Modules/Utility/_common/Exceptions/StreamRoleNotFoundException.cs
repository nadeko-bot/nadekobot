#nullable disable
namespace NadekoBot.Modules.Utility.Common.Exceptions;

public class StreamRoleNotFoundException : Exception
{
    public StreamRoleNotFoundException()
        : base("Stream role wasn't found.")
    {
    }

    public StreamRoleNotFoundException(string message)
        : base(message)
    {
    }

    public StreamRoleNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}