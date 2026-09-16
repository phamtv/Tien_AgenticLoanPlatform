using System.Collections.Concurrent;

namespace LoanPlatform.Common.Logging;

public record TraceEntry(
    long Seq,
    DateTimeOffset Timestamp,
    string Service,
    string? ApplicationId,
    string Step,
    string Message,
    object? Data
);

/// <summary>
/// In-memory, per-process ring buffer of every TraceLogger.Trace() call this
/// service instance has made — feeds the UI's live "Logs" tab
/// (TraceController.Recent below, polled from ui/src/app/app.ts).
///
/// Deliberately NOT the same thing as the console/JSON log output: this is
/// populated directly from TraceLogger.Trace()'s own parameters, so it's
/// unaffected by Microsoft.Extensions.Logging's category/level filtering
/// (console output still goes through that pipeline separately — see
/// LoggingExtensions.cs and TraceLogger.cs). Still gated by the same
/// TRACE_LOGGING compile constant, since population only happens from
/// inside Trace(), whose call sites are stripped entirely when
/// TRACE_LOGGING isn't defined.
///
/// One buffer per service PROCESS (not shared across services — there's no
/// cross-service database here, matching this platform's service-owns-its-
/// data convention). ServiceName is set once at startup in each service's
/// Program.cs. Bounded at Capacity entries so a long-running service
/// doesn't leak memory; old entries just age out, same tradeoff as any
/// in-memory-only ring buffer in this repo (see the in-memory repositories).
/// </summary>
public static class TraceBuffer
{
    private const int Capacity = 2000;
    private static long _seq;
    private static readonly ConcurrentQueue<TraceEntry> _entries = new();

    /// <summary>Set once at startup — e.g. TraceBuffer.ServiceName = "Origination"; in Program.cs.</summary>
    public static string ServiceName { get; set; } = "Unknown";

    public static void Add(string applicationId, string step, string message, object? data)
    {
        var entry = new TraceEntry(
            Seq: Interlocked.Increment(ref _seq),
            Timestamp: DateTimeOffset.UtcNow,
            Service: ServiceName,
            ApplicationId: applicationId,
            Step: step,
            Message: message,
            Data: data);

        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
    }

    /// <summary>Entries with Seq strictly greater than <paramref name="afterSeq"/>, oldest first — for incremental polling.</summary>
    public static IReadOnlyList<TraceEntry> Since(long afterSeq) =>
        _entries.Where(e => e.Seq > afterSeq).OrderBy(e => e.Seq).ToList();

    /// <summary>The most recent <paramref name="count"/> entries, oldest first — for an initial page load with no cursor yet.</summary>
    public static IReadOnlyList<TraceEntry> Recent(int count) =>
        _entries.ToArray() is var snapshot && snapshot.Length > count
            ? snapshot[^count..]
            : snapshot;
}
