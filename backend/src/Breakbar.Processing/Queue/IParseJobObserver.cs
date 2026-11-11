using Breakbar.Processing.EliteInsights;

namespace Breakbar.Processing.Queue;

/// <summary>
/// Receives job lifecycle events from <see cref="ParseWorkerPool"/>.
/// The API implements this to update SQLite and push SSE status updates. Must not throw.
/// </summary>
public interface IParseJobObserver
{
    ValueTask OnStartedAsync(ParseJob job);

    /// <summary>EI finished; <paramref name="result"/> tells whether the log parsed successfully.</summary>
    ValueTask OnCompletedAsync(ParseJob job, EliteInsightsResult result);

    /// <summary>Unexpected exception while parsing (a bug, not a bad log).</summary>
    ValueTask OnFailedAsync(ParseJob job, Exception exception);

    /// <summary>The service is shutting down; the job did not finish and should be re-queued on the next start.</summary>
    ValueTask OnInterruptedAsync(ParseJob job);
}
