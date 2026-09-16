using Microsoft.Extensions.Logging;

namespace LoanPlatform.Common.Logging;

/// <summary>
/// Same structured JSON console logging pattern used in the earlier
/// backend-dotnet demo — no Serilog/NuGet dependency needed, built
/// entirely from the shared framework's Microsoft.Extensions.Logging.Console.
/// </summary>
public static class LoggingExtensions
{
    public static ILoggingBuilder AddLoanPlatformLogging(this ILoggingBuilder builder)
    {
        builder.ClearProviders();
        builder.AddJsonConsole(options =>
        {
            options.IncludeScopes = false;
            // BUG FIX: TimestampFormat's trailing "Z" claims UTC, but
            // UseUtcTimestamp defaults to false — every log line was
            // actually stamped in the container/host's LOCAL time with a
            // "Z" suffix falsely claiming UTC. Harmless on a machine
            // already running UTC, actively misleading (and wrong by
            // whatever the local offset is) anywhere else, and especially
            // bad for correlating timestamps across services running on
            // different hosts/timezones. UseUtcTimestamp=true makes the
            // "Z" actually true.
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        return builder;
    }
}
