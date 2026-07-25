using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace HackyMessage.Configuration;

public sealed class LoggingConfiguration
{
    public void ConfigureAndStart()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Async(a
                    => a.Console(
                        theme: AnsiConsoleTheme.Sixteen,
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{MachineName} Thread:{ThreadId:000}] ({SourceContext}) {Message:lj}{NewLine}{Exception}"),
                bufferSize: 10000,
                blockWhenFull: false)
            .CreateLogger();
    }
    
    public void StopAndFlush()
    {
        Log.CloseAndFlush();
    }
}