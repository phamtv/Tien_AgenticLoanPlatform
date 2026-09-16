using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LoanPlatform.Common.Logging;

/// <summary>
/// Per-application execution trace: which step ran, in which service, in
/// what order, with what values — for debugging the event-driven flow
/// across all four services. This is deliberately separate from the
/// ordinary Information-level milestone logs already in each
/// controller/handler (those say "a decision was made"; this says
/// "RiskEngine.Evaluate was entered with these inputs and returned this").
///
/// TWO independent gates control this, on purpose:
///
///   1. COMPILE-TIME (the actual #define equivalent): every call site is
///      wrapped in [Conditional("TRACE_LOGGING")]. When a service's
///      .csproj doesn't define TRACE_LOGGING, the C# compiler strips every
///      call to Trace() below entirely — no IL emitted, not a runtime
///      no-op — so a production build has zero trace-logging code or
///      overhead in the binary at all. Controlled per service via:
///        dotnet build -p:TraceLogging=false
///      or the TRACE_LOGGING docker-compose build arg (see each service's
///      .csproj and Dockerfile). Defaults to true (mirrors this repo's
///      existing UseSqlServer pattern) — flip to false for a production
///      build.
///
///   2. RUNTIME, for the reliable path: every call also writes directly
///      into TraceBuffer (below), an in-memory ring buffer completely
///      independent of Microsoft.Extensions.Logging's category/level
///      filtering. That's what TraceController exposes and what the UI's
///      Logs tab polls — so it always captures every trace call once
///      TRACE_LOGGING is compiled in, with nothing further to configure.
///
/// Console/docker-logs output is a SEPARATE, best-effort byproduct: Trace()
/// also calls logger.LogTrace(...) at LogLevel.Trace under the CALLING
/// class's own category (e.g. "OriginationService.Controllers.
/// ApplicationsController" for the this-ILogger overload below) — not
/// under "LoanPlatform.Trace" unless you use the ILoggerFactory overload.
/// appsettings.json's "LoanPlatform.Trace": "Trace" override therefore only
/// affects the ILoggerFactory overload; for the far more common
/// this-ILogger overload, whether a line reaches the console depends on
/// that calling category's own configured level (Default is
/// "Information", which filters Trace out). If you want these lines in
/// `docker compose logs` too, not just the UI's Logs tab, bump
/// Logging:LogLevel:Default to "Trace" (or add the specific class's
/// category) — the Logs tab itself doesn't need that; it always works via
/// TraceBuffer regardless.
///
/// Usage (any class that already has an ILogger&lt;T&gt; injected):
///   _logger.Trace(applicationId, "RiskEngine.Evaluate.Start",
///       "Running risk engine", new { creditScore, monthlyIncome });
/// </summary>
public static class TraceLogger
{
    public static readonly EventId TraceEventId = new(90000, "LoanPlatformTrace");
    private const string CategoryName = "LoanPlatform.Trace";

    [Conditional("TRACE_LOGGING")]
    public static void Trace(this ILogger logger, string applicationId, string step, string message, object? data = null)
    {
        // Timestamp is stamped explicitly here (UTC, round-trip "O" format)
        // rather than relying solely on the console formatter's own
        // timestamp — see LoggingExtensions.AddLoanPlatformLogging, which
        // that timestamp depends on being configured correctly (it wasn't,
        // until the UseUtcTimestamp fix there). Stamping it into the trace
        // payload itself means a trace line is still correctly timestamped
        // even if it's later extracted, piped to a file, or the console
        // formatter's own settings change.
        var timestamp = DateTimeOffset.UtcNow;
        logger.LogTrace(TraceEventId, "TRACE [{Timestamp:O}] [{ApplicationId}] {Step}: {Message} {@Data}",
            timestamp, applicationId, step, message, data);
        TraceBuffer.Add(applicationId, step, message, data);
    }

    /// <summary>
    /// Same as <see cref="Trace(ILogger,string,string,string,object?)"/>
    /// but always logs under the fixed "LoanPlatform.Trace" category
    /// regardless of the caller's own ILogger&lt;T&gt; category — use this
    /// when you want every trace line groupable under one category name
    /// across all services (e.g. to set just "LoanPlatform.Trace": "Trace"
    /// once in appsettings rather than per calling class).
    /// </summary>
    [Conditional("TRACE_LOGGING")]
    public static void Trace(ILoggerFactory loggerFactory, string applicationId, string service, string step, string message, object? data = null)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var logger = loggerFactory.CreateLogger(CategoryName);
        logger.LogTrace(TraceEventId, "TRACE [{Timestamp:O}] [{ApplicationId}] {Service}.{Step}: {Message} {@Data}",
            timestamp, applicationId, service, step, message, data);
        TraceBuffer.Add(applicationId, step, message, data);
    }
}
