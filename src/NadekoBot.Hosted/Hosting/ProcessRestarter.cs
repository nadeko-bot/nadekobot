using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace NadekoBot.Hosted;

public interface IProcessRestarter
{
    void RestartAfter(HttpContext http);
}

public sealed class ProcessRestarter(IHostApplicationLifetime lifetime) : IProcessRestarter
{
    public void RestartAfter(HttpContext http)
        => http.Response.OnCompleted(StopInternalAsync);

    public static void StopForSupervisor(IHostApplicationLifetime lifetime)
    {
        Log.Information("Stopping so the supervisor starts the bot again");
        lifetime.StopApplication();
    }

    private Task StopInternalAsync()
    {
        StopForSupervisor(lifetime);
        return Task.CompletedTask;
    }
}
