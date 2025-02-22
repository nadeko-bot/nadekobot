using System;
using System.Text;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace NadekoBot.Services
{
    public static class LogSetup
    {
        public static void SetupLogger(object source)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File("coord.log", LogEventLevel.Information, 
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 10_000_000)
                .WriteTo.Console(LogEventLevel.Information,
                    theme: GetTheme(),
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] | #{LogSource} | {Message:lj}{NewLine}{Exception}")
                .Enrich.WithProperty("LogSource", source)
                .CreateLogger();
            
            Console.OutputEncoding = Encoding.UTF8;
        }

        private static ConsoleTheme GetTheme()
        {
            if(Environment.OSVersion.Platform == PlatformID.Unix)
                return AnsiConsoleTheme.Code;
            
            
#if DEBUG
            return AnsiConsoleTheme.Code;
#else
            return ConsoleTheme.None;
#endif
        }
    }
}
