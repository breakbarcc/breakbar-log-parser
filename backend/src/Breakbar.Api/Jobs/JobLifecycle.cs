using Breakbar.Api.Persistence;
using Breakbar.Api.Options;
using Breakbar.Processing.EliteInsights;
using Breakbar.Processing.Queue;

namespace Breakbar.Api.Jobs;

/// <summary>
/// Connects the worker pool to SQLite, the file system and SSE: records status changes, stores finished
/// reports (deduplicated by file hash), cleans up uploads and notifies connected clients.
/// </summary>
public sealed partial class JobLifecycle(
    JobStore store,
    FairParseQueue queue,
    JobEventHub hub,
    EliteInsightsAdapter eliteInsights,
    StorageOptions storage,
    ReportsOptions reports,
    ILogger<JobLifecycle> logger) : IParseJobObserver
{
    public ValueTask OnStartedAsync(ParseJob job)
    {
        store.MarkRunning(Key(job));
        Publish(job);
        hub.PublishPositions(); // everybody behind this job moved up
        return ValueTask.CompletedTask;
    }

    public ValueTask OnCompletedAsync(ParseJob job, EliteInsightsResult result)
    {
        var record = store.GetJob(Key(job));
        if (record is null)
        {
            return ValueTask.CompletedTask;
        }

        if (result.Success)
        {
            string storedId = store.InsertReportOrGetExisting(new ReportRecord
            {
                Id = record.ReportId,
                Sha256 = record.Sha256,
                Anonymized = record.Anonymized,
                EncounterId = result.EncounterId,
                LogName = result.LogName,
                EncounterSuccess = result.EncounterSuccess,
                DurationMs = result.DurationMs,
                EiVersion = eliteInsights.EliteInsightsVersion.ToString(),
                HtmlBytes = FileSize(result.HtmlPath),
                JsonBytes = FileSize(result.JsonGzPath),
                CreatedAt = store.Now(),
            });
            if (storedId != record.ReportId)
            {
                // The same file was parsed in parallel and stored first; keep that report.
                DeleteDirectory(job.OutputDirectory);
            }
            store.MarkDone(record.Id, storedId);
        }
        else
        {
            LogParseFailed(job.Id, result.FailureReason);
            store.MarkFailed(record.Id, result.FailureReason ?? "Parsing failed.");
            DeleteDirectory(job.OutputDirectory);
        }

        DeleteFile(job.LogPath);
        Publish(job);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnFailedAsync(ParseJob job, Exception exception)
    {
        store.MarkFailed(Key(job), "Internal error while parsing this log.");
        DeleteDirectory(job.OutputDirectory);
        DeleteFile(job.LogPath);
        Publish(job);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnInterruptedAsync(ParseJob job)
    {
        // Keep the upload; the job is re-queued on the next start (see RecoverPendingJobs).
        store.MarkQueued(Key(job));
        DeleteDirectory(job.OutputDirectory);
        return ValueTask.CompletedTask;
    }

    /// <summary>Re-enqueues jobs that were waiting or running when the service stopped. Call once before the workers start.</summary>
    public int RecoverPendingJobs()
    {
        int recovered = 0;
        foreach (var record in store.ListPending())
        {
            if (!File.Exists(record.UploadPath))
            {
                store.MarkFailed(record.Id, "Upload was lost during a restart, please upload again.");
                continue;
            }
            store.MarkQueued(record.Id);
            string outputDirectory = Path.GetFullPath(storage.ReportDirectory(record.ReportId));
            var job = new ParseJob(Guid.ParseExact(record.Id, "N"), record.UserId, record.UploadPath, outputDirectory, record.EstimateBytes, record.Anonymized);
            if (queue.Enqueue(job) == EnqueueResult.Accepted)
            {
                recovered++;
            }
            else
            {
                store.MarkFailed(record.Id, "Log is too large for this server (memory limit).");
            }
        }
        return recovered;
    }

    private void Publish(ParseJob job)
    {
        if (store.GetView(Key(job)) is { } view)
        {
            hub.PublishJob(view.UserId, JobDto.From(view, queue.GetPosition(job.Id), reports));
        }
    }

    private static string Key(ParseJob job) => job.Id.ToString("N");

    private static long FileSize(string? path) => path is not null && File.Exists(path) ? new FileInfo(path).Length : 0;

    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // best effort; the retention job cleans up leftovers
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} could not be parsed: {Reason}")]
    private partial void LogParseFailed(Guid jobId, string? reason);
}
