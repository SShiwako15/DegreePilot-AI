using System.Diagnostics;

namespace DegreePilot.Api.Application;

// Fixed-cardinality, process-local counts. No student IDs, URLs or request contents.
public sealed class OperationalMetrics
{
    private readonly long started = Stopwatch.GetTimestamp();
    private long requests;
    private long serverErrors;
    private long rateLimited;
    public void Observe(int status)
    {
        Interlocked.Increment(ref requests);
        if (status >= 500) Interlocked.Increment(ref serverErrors);
        if (status == 429) Interlocked.Increment(ref rateLimited);
    }
    public object Snapshot() => new
    {
        requests = Interlocked.Read(ref requests),
        serverErrors = Interlocked.Read(ref serverErrors),
        rateLimited = Interlocked.Read(ref rateLimited),
        uptimeSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds
    };
}
