using Breakbar.Api.Options;
using Breakbar.Api.Persistence;

namespace Breakbar.Api.Jobs;

/// <summary>
/// Enforces the retention promised in the privacy policy (docs/PLAN.md 4c), once at startup and then every
/// <see cref="RetentionOptions.Interval"/>:
/// 1. deletes every report older than <see cref="RetentionOptions.ReportDays"/> days, for all users,
/// 2. deletes the job records (file name, hash, browser id ...) of that age; waiting and running jobs stay,
/// 3. as a safety net deletes the oldest reports while all reports use more than <see cref="RetentionOptions.MaxReportsBytes"/>,
/// 4. removes files nothing refers to any more (uploads of finished jobs, report folders after a crash).
/// A job that is younger than its report (same file uploaded again later) becomes "expired" when the report goes.
/// </summary>
public sealed partial class RetentionService(
    JobStore store,
    StorageOptions storage,
    RetentionOptions options,
    TimeProvider time,
    ILogger<RetentionService> logger) : BackgroundService
{
    /// <summary>Files younger than this are never treated as orphans (an upload may be in progress).</summary>
    private static readonly TimeSpan OrphanMinAge = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Interval, time);
        do
        {
            try
            {
                RunOnce();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                LogRetentionFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public void RunOnce()
    {
        int deleted = 0;

        long cutoff = (time.GetUtcNow() - options.DeleteAfter).ToUnixTimeMilliseconds();
        foreach (var report in store.ListReportsBefore(cutoff))
        {
            Delete(report.Id);
            deleted++;
        }
        int jobs = store.DeleteFinishedJobsBefore(cutoff);

        long used = store.GetReportsBytes();
        if (used > options.MaxReportsBytes)
        {
            long target = options.MaxReportsBytes / 10 * 9;
            while (used > target)
            {
                var oldest = store.ListOldestReports(100);
                if (oldest.Count == 0)
                {
                    break;
                }
                foreach (var report in oldest)
                {
                    Delete(report.Id);
                    deleted++;
                    used -= report.HtmlBytes + report.JsonBytes;
                    if (used <= target)
                    {
                        break;
                    }
                }
            }
        }

        int orphans = DeleteOrphans();
        if (deleted > 0 || jobs > 0 || orphans > 0)
        {
            LogRetentionDone(deleted, jobs, orphans, store.GetReportsBytes() / (1024 * 1024));
        }
    }

    private void Delete(string reportId)
    {
        store.DeleteReport(reportId);
        DeleteDirectory(storage.ReportDirectory(reportId));
    }

    private int DeleteOrphans()
    {
        int count = 0;
        var cutoff = time.GetUtcNow().UtcDateTime - OrphanMinAge;

        if (Directory.Exists(storage.UploadsDirectory))
        {
            var pending = store.ListPendingUploadPaths();
            foreach (var file in new DirectoryInfo(storage.UploadsDirectory).EnumerateFiles())
            {
                if (file.LastWriteTimeUtc < cutoff && !pending.Contains(file.FullName))
                {
                    file.Delete();
                    count++;
                }
            }
        }

        if (Directory.Exists(storage.ReportsDirectory))
        {
            var live = store.ListLiveReportIds();
            foreach (var dir in new DirectoryInfo(storage.ReportsDirectory).EnumerateDirectories())
            {
                if (dir.LastWriteTimeUtc < cutoff && !live.Contains(dir.Name))
                {
                    DeleteDirectory(dir.FullName);
                    count++;
                }
            }
        }
        return count;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention deleted {Reports} reports, {Jobs} job records and {Orphans} orphaned files; reports now use {UsedMb} MB")]
    private partial void LogRetentionDone(int reports, int jobs, int orphans, long usedMb);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retention run failed")]
    private partial void LogRetentionFailed(Exception exception);
}
