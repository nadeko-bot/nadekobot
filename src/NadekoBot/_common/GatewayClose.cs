#nullable enable
namespace NadekoBot.Common;

// Discord.Net gives a close of a running gateway to Log and Disconnected as a WebSocketException which wraps the WebSocketClosedException.
public static class GatewayClose
{
    public static int? CodeOf(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is WebSocketClosedException closed)
                return closed.CloseCode;
        }

        return null;
    }
}
