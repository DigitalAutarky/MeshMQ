using HackyMessage.Configuration;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace TestSuite;

[SetUpFixture]
public class TestConfiguration
{
    private readonly Configuration _configuration = new();

    [OneTimeSetUp]
    public void BeforeAnyTest()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Console(
                applyThemeToRedirectedOutput: true,
                theme: AnsiConsoleTheme.Sixteen,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{MachineName} Thread:{ThreadId:000}] ({SourceContext}) {Message:lj}{NewLine}{Exception}") 
            .CreateLogger();
    }

    [OneTimeTearDown]
    public void AfterAllTests()
    {
        Log.CloseAndFlush();
    }
}