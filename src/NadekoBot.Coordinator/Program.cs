using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using NadekoBot.Coordinator;
using NadekoBot.Services;
using Serilog;

// Additional configuration is required to successfully run gRPC on macOS.
// For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682
static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseStartup<CoordStartup>();
        });

LogSetup.SetupLogger("coord");
Log.Information("Starting coordinator... Pid: {ProcessId}", Environment.ProcessId);

CreateHostBuilder(args).Build().Run();