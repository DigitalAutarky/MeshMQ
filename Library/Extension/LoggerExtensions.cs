using Serilog;
using Serilog.Core;

namespace HackyMessage.Extension;

public static class LoggerExtensions
{
    public static ILogger ForFriendlyContext<T>(this ILogger logger)
    {
        // "SourceContext" is the built-in Serilog property name
        return logger.ForContext(Constants.SourceContextPropertyName, typeof(T).GetFriendlyName());
    }
}