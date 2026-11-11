using Dapper;

namespace Breakbar.Api.Persistence;

public static class JobStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

/// <summary>Row of the jobs table. <see cref="ReportId"/> is reserved at upload and final once the job is done.</summary>
public sealed record JobRecord
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public string? BatchId { get; init; }
    public required string FileName { get; init; }
    public required string UploadPath { get; init; }
    public required string Sha256 { get; init; }
    public bool Anonymized { get; init; }
    public long FileBytes { get; init; }
    public long EvtcBytes { get; init; }
    public long EstimateBytes { get; init; }
    public required string Status { get; init; }
    public required string ReportId { get; init; }
    public string? Error { get; init; }
    public long CreatedAt { get; init; }
    public long? StartedAt { get; init; }
    public long? FinishedAt { get; init; }
}

public sealed class ReportRecord
{
    public required string Id { get; init; }
    public required string Sha256 { get; init; }
    public bool Anonymized { get; init; }
    public string? EncounterId { get; init; }
    public string? LogName { get; init; }
    public bool? EncounterSuccess { get; init; }
    public long? DurationMs { get; init; }
    public required string EiVersion { get; init; }
    public long HtmlBytes { get; init; }
    public long JsonBytes { get; init; }
    public long CreatedAt { get; init; }
}

/// <summary>Job joined with its report, as shown to users.</summary>
public sealed class JobView
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public string? BatchId { get; init; }
    public required string FileName { get; init; }
    public long FileBytes { get; init; }
    public required string Status { get; init; }
    public required string ReportId { get; init; }
    public bool Anonymized { get; init; }
    public string? Error { get; init; }
    public long CreatedAt { get; init; }
    public long? StartedAt { get; init; }
    public long? FinishedAt { get; init; }
    public string? EncounterId { get; init; }
    public string? LogName { get; init; }
    public bool? EncounterSuccess { get; init; }
    public long? DurationMs { get; init; }
}

/// <summary>All SQL for jobs and reports. Times are Unix milliseconds (UTC).</summary>
public sealed class JobStore(Database database, TimeProvider time)
{
    private const string ViewSelect = """
        SELECT j.id, j.user_id, j.batch_id, j.file_name, j.file_bytes, j.status, j.report_id, j.error,
               j.anonymized, j.created_at, j.started_at, j.finished_at,
               r.encounter_id, r.log_name, r.encounter_success, r.duration_ms
        FROM jobs j
        LEFT JOIN reports r ON r.id = j.report_id AND j.status = 'done'
        """;

    public long Now() => time.GetUtcNow().ToUnixTimeMilliseconds();

    public void Insert(JobRecord job)
    {
        using var db = database.Open();
        db.Execute("""
            INSERT INTO jobs (id, user_id, batch_id, file_name, upload_path, sha256, anonymized, file_bytes, evtc_bytes,
                              estimate_bytes, status, report_id, error, created_at, started_at, finished_at)
            VALUES (@Id, @UserId, @BatchId, @FileName, @UploadPath, @Sha256, @Anonymized, @FileBytes, @EvtcBytes,
                    @EstimateBytes, @Status, @ReportId, @Error, @CreatedAt, @StartedAt, @FinishedAt)
            """, job);
    }

    public JobRecord? GetJob(string id)
    {
        using var db = database.Open();
        return db.QuerySingleOrDefault<JobRecord>("SELECT * FROM jobs WHERE id = @id", new { id });
    }

    public JobView? GetView(string id)
    {
        using var db = database.Open();
        return db.QuerySingleOrDefault<JobView>($"{ViewSelect} WHERE j.id = @id", new { id });
    }

    public IReadOnlyList<JobView> ListForUser(string userId, string? batchId, int limit)
    {
        using var db = database.Open();
        return db.Query<JobView>($"""
            {ViewSelect}
            WHERE j.user_id = @userId AND (@batchId IS NULL OR j.batch_id = @batchId)
            ORDER BY j.created_at DESC
            LIMIT @limit
            """, new { userId, batchId, limit }).AsList();
    }

    /// <summary>Jobs that must be put back into the queue after a restart, oldest first.</summary>
    public IReadOnlyList<JobRecord> ListPending()
    {
        using var db = database.Open();
        return db.Query<JobRecord>("SELECT * FROM jobs WHERE status IN ('queued', 'running') ORDER BY created_at").AsList();
    }

    public void MarkRunning(string id) => SetStatus(id, JobStatus.Running, startedAt: Now());

    public void MarkQueued(string id)
    {
        using var db = database.Open();
        db.Execute("UPDATE jobs SET status = 'queued', started_at = NULL WHERE id = @id", new { id });
    }

    public void MarkDone(string id, string reportId)
    {
        using var db = database.Open();
        db.Execute("UPDATE jobs SET status = 'done', report_id = @reportId, error = NULL, finished_at = @now WHERE id = @id",
            new { id, reportId, now = Now() });
    }

    public void MarkFailed(string id, string error) => SetStatus(id, JobStatus.Failed, error: error, finishedAt: Now());

    public void MarkCancelled(string id) => SetStatus(id, JobStatus.Cancelled, finishedAt: Now());

    public ReportRecord? FindReport(string sha256, bool anonymized)
    {
        using var db = database.Open();
        return db.QuerySingleOrDefault<ReportRecord>("SELECT * FROM reports WHERE sha256 = @sha256 AND anonymized = @anonymized", new { sha256, anonymized });
    }

    public bool ReportExists(string id)
    {
        using var db = database.Open();
        return db.ExecuteScalar<long>("SELECT COUNT(*) FROM reports WHERE id = @id", new { id }) > 0;
    }

    /// <summary>
    /// Stores the report unless one with the same file hash and options exists (two identical uploads parsed in parallel).
    /// Returns the id of the report that is now stored for this hash.
    /// </summary>
    public string InsertReportOrGetExisting(ReportRecord report)
    {
        using var db = database.Open();
        db.Execute("""
            INSERT INTO reports (id, sha256, anonymized, encounter_id, log_name, encounter_success, duration_ms,
                                 ei_version, html_bytes, json_bytes, created_at)
            VALUES (@Id, @Sha256, @Anonymized, @EncounterId, @LogName, @EncounterSuccess, @DurationMs,
                    @EiVersion, @HtmlBytes, @JsonBytes, @CreatedAt)
            ON CONFLICT (sha256, anonymized) DO NOTHING
            """, report);
        return db.ExecuteScalar<string>("SELECT id FROM reports WHERE sha256 = @Sha256 AND anonymized = @Anonymized", report)!;
    }

    private void SetStatus(string id, string status, string? error = null, long? startedAt = null, long? finishedAt = null)
    {
        using var db = database.Open();
        db.Execute("""
            UPDATE jobs SET status = @status,
                            error = COALESCE(@error, error),
                            started_at = COALESCE(@startedAt, started_at),
                            finished_at = COALESCE(@finishedAt, finished_at)
            WHERE id = @id
            """, new { id, status, error, startedAt, finishedAt });
    }
}
