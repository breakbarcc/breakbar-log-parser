using Breakbar.Api.Options;
using Breakbar.Api.Persistence;

namespace Breakbar.Api.Jobs;

/// <summary>Job as returned by the API and pushed via SSE.</summary>
public sealed record JobDto(
    string Id,
    string FileName,
    long FileBytes,
    string Status,
    int? Position,
    string? BatchId,
    bool Anonymized,
    string? ReportId,
    string? ReportUrl,
    string? JsonUrl,
    string? EncounterId,
    string? LogName,
    bool? EncounterSuccess,
    long? DurationMs,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt)
{
    public static JobDto From(JobView job, int? position, ReportsOptions reports)
    {
        bool done = job.Status == JobStatus.Done;
        return new JobDto(
            job.Id,
            job.FileName,
            job.FileBytes,
            job.Status,
            job.Status == JobStatus.Queued ? position : null,
            job.BatchId,
            job.Anonymized,
            done ? job.ReportId : null,
            done ? reports.ReportUrl(job.ReportId) : null,
            done ? reports.ReportUrl(job.ReportId) + "/json" : null,
            job.EncounterId,
            job.LogName,
            job.EncounterSuccess,
            job.DurationMs,
            job.Error,
            DateTimeOffset.FromUnixTimeMilliseconds(job.CreatedAt),
            job.FinishedAt is { } f ? DateTimeOffset.FromUnixTimeMilliseconds(f) : null);
    }
}

/// <summary>Queue positions of a user's waiting jobs, pushed via SSE whenever the queue moves.</summary>
public sealed record PositionsDto(IReadOnlyDictionary<string, int> Positions);
