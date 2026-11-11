using System.Runtime;
using Breakbar.Processing.EliteInsights;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Breakbar.Processing.Queue;

/// <summary>
/// Runs <see cref="ParseQueueOptions.MaxParallel"/> workers. Each worker takes the next job from the
/// <see cref="FairParseQueue"/>, parses it with Elite Insights on its own thread and reports to the
/// <see cref="IParseJobObserver"/>. A failing job never stops a worker.
/// </summary>
public sealed partial class ParseWorkerPool(
    FairParseQueue queue,
    IEliteInsightsParser parser,
    IParseJobObserver observer,
    ParseQueueOptions options,
    ILogger<ParseWorkerPool> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(1, options.MaxParallel)
            .Select(id => Task.Run(() => RunWorkerAsync(id, stoppingToken), CancellationToken.None));
        return Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(int workerId, CancellationToken stoppingToken)
    {
        LogWorkerStarted(workerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            ParseLease lease;
            try
            {
                lease = await queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using (lease)
            {
                await ProcessAsync(workerId, lease.Job, stoppingToken).ConfigureAwait(false);
                if (lease.IsExclusive)
                {
                    // Big logs leave a large managed heap behind; give the memory back before the next job.
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                }
            }
        }
        LogWorkerStopped(workerId);
    }

    private async Task ProcessAsync(int workerId, ParseJob job, CancellationToken stoppingToken)
    {
        try
        {
            await observer.OnStartedAsync(job).ConfigureAwait(false);
            LogJobStarted(workerId, job.Id, job.UserId, job.EstimatedMemoryBytes / (1024 * 1024));

            var result = parser.Parse(job.LogPath, job.OutputDirectory, job.AnonymizePlayers, stoppingToken);

            LogJobFinished(workerId, job.Id, result.Success, (long)result.Elapsed.TotalMilliseconds);
            await observer.OnCompletedAsync(job, result).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogJobInterrupted(workerId, job.Id);
            await observer.OnInterruptedAsync(job).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogJobCrashed(ex, workerId, job.Id);
            try
            {
                await observer.OnFailedAsync(job, ex).ConfigureAwait(false);
            }
            catch (Exception observerEx)
            {
                LogObserverFailed(observerEx, job.Id);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Parse worker {WorkerId} started")]
    private partial void LogWorkerStarted(int workerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Parse worker {WorkerId} stopped")]
    private partial void LogWorkerStopped(int workerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker {WorkerId} parsing job {JobId} for {UserId} (estimate {EstimateMb} MB)")]
    private partial void LogJobStarted(int workerId, Guid jobId, string userId, long estimateMb);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker {WorkerId} finished job {JobId}: success={Success} in {ElapsedMs} ms")]
    private partial void LogJobFinished(int workerId, Guid jobId, bool success, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker {WorkerId} interrupted job {JobId} because the service is stopping")]
    private partial void LogJobInterrupted(int workerId, Guid jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Worker {WorkerId} crashed on job {JobId}")]
    private partial void LogJobCrashed(Exception exception, int workerId, Guid jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Observer failed while reporting job {JobId}")]
    private partial void LogObserverFailed(Exception exception, Guid jobId);
}
