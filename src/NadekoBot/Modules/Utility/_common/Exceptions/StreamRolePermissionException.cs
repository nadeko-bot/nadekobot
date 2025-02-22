#nullable disable
namespace NadekoBot.Modules.Utility.Common.Exceptions;

public class StreamRolePermissionException : Exception
{
    public StreamRolePermissionException()
        : base("Stream role was unable to be applied.")
    {
    }

    public StreamRolePermissionException(string message)
        : base(message)
    {
    }

    public StreamRolePermissionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}