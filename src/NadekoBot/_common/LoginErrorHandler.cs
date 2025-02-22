#nullable disable
using System.Net;
using System.Runtime.CompilerServices;

namespace NadekoBot.Common;

public class LoginErrorHandler
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Handle(Exception ex)
        => Log.Fatal(ex, "A fatal error has occurred while attempting to connect to Discord");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Handle(HttpException ex)
    {
        switch (ex.HttpCode)
        {
            case HttpStatusCode.Unauthorized:
                Log.Error("Your bot token is wrong.\n"
                          + "You can find the bot token under the Bot tab in the developer page.\n"
                          + "Fix your token in the credentials file and restart the bot");
                break;

            case HttpStatusCode.BadRequest:
                Log.Error("Something has been incorrectly formatted in your credentials file.\n"
                          + "Use the JSON Guide as reference to fix it and restart the bot");
                Log.Error("If you are on Linux, make sure Redis is installed and running");
                break;

            case HttpStatusCode.RequestTimeout:
                Log.Error("The request timed out. Make sure you have no external program blocking the bot "
                          + "from connecting to the internet");
                break;

            case HttpStatusCode.ServiceUnavailable:
            case HttpStatusCode.InternalServerError:
                Log.Error("Discord is having internal issues. Please, try again later");
                break;

            case HttpStatusCode.TooManyRequests:
                Log.Error("Your bot has been ratelimited by Discord. Please, try again later.\n"
                          + "Global ratelimits usually last for an hour");
                break;

            default:
                Log.Warning("An error occurred while attempting to connect to Discord");
                break;
        }

        Log.Fatal(ex, "Fatal error occurred while loading credentials");
    }
}