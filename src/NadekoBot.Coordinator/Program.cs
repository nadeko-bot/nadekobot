using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NadekoBot.Coordinator;
using NadekoBot.Coordinator.Api;
using NadekoBot.Services;
using Serilog;

static IHostBuilder CreateHostBuilder(string[] args, CoordApiEndpoint api) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.ConfigureServices(services => services.AddSingleton(api));
            webBuilder.ConfigureKestrel(kestrel =>
            {
                // The shard port speaks HTTP/2 without TLS, which a dashboard client does not speak, so the API has its own port.
                if (api.Enabled)
                    kestrel.Listen(api.Address!, api.Port, listen => listen.Protocols = HttpProtocols.Http1);
            });
            webBuilder.UseStartup<CoordStartup>();
        });

LogSetup.SetupLogger("coord");
Log.Information("Starting coordinator... Pid: {ProcessId}", Environment.ProcessId);

var api = CoordApiEndpoint.From(CoordinatorRunner.ReadConfigFile());
if (api.Enabled)
    Log.Information("Dashboard API listens on {Address}:{Port}", api.Address, api.Port);

CreateHostBuilder(args, api).Build().Run();
