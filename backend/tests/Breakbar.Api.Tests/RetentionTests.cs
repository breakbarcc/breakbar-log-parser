using Breakbar.Api.Jobs;
using Breakbar.Api.Options;
using Breakbar.Api.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Breakbar.Api.Tests;

/// <summary>The privacy policy promises: reports and job records are deleted after at most 7 days.</summary>
public sealed class RetentionTests : IDisposable
{
    private const long MB = 1024L * 1024L;
    private readonly StorageOptions _storage = new() { DataDirectory = Directory.CreateTempSubdirectory("breakbar-retention-").FullName };
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly JobStore _store;

    public RetentionTests()
    {
        var database = new Database(_storage);
        database.Initialize();
        _store = new JobStore(database, _time);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_storage.DataDirectory, recursive: true);
    }

    private RetentionService Service(long maxReportsBytes = long.MaxValue, int reportDays = 7, TimeSpan? interval = null)
        => new(_store, _storage,
            new RetentionOptions { MaxReportsBytes = maxReportsBytes, ReportDays = reportDays, Interval = interval ?? TimeSpan.FromMinutes(15) },
            _time, NullLogger<RetentionService>.Instance);

    private long Ago(TimeSpan age) => (_time.GetUtcNow() - age).ToUnixTimeMilliseconds();

    /// <summary>A finished report of <paramref name="sizeMb"/> MB created <paramref name="age"/> ago, with one done job (created <paramref name="jobAge"/> ago, default the same).</summary>
    private (string ReportId, string JobId) AddReport(string user, TimeSpan age, long sizeMb = 1, TimeSpan? jobAge = null)
    {
        string reportId = ShortId.New();
        _store.InsertReportOrGetExisting(new ReportRecord
        {
            Id = reportId,
            Sha256 = Guid.NewGuid().ToString("N"),
            EiVersion = "3.32.0.0",
            HtmlBytes = sizeMb * MB,
            JsonBytes = 0,
            CreatedAt = Ago(age),
        });
        string jobId = AddJob(user, JobStatus.Done, jobAge ?? age, reportId);
        Directory.CreateDirectory(_storage.ReportDirectory(reportId));
        File.WriteAllText(Path.Combine(_storage.ReportDirectory(reportId), "report.html"), "<html>");
        return (reportId, jobId);
    }

    private string AddJob(string user, string status, TimeSpan age, string? reportId = null)
    {
        string jobId = Guid.NewGuid().ToString("N");
        _store.Insert(new JobRecord
        {
            Id = jobId,
            UserId = user,
            FileName = "x.zevtc",
            UploadPath = "",
            Sha256 = "s",
            Status = status,
            ReportId = reportId ?? ShortId.New(),
            CreatedAt = Ago(age),
        });
        return jobId;
    }

    private bool ReportExists(string reportId) => _store.ReportExists(reportId) || Directory.Exists(_storage.ReportDirectory(reportId));

    [Fact]
    public void Reports_older_than_the_retention_period_are_deleted_for_every_user()
    {
        var browser = AddReport("0123456789abcdef0123456789abcdef", TimeSpan.FromDays(8));
        var service = AddReport("svc:voidlog", TimeSpan.FromDays(8));
        var fresh = AddReport("0123456789abcdef0123456789abcdef", TimeSpan.FromDays(6));

        Service().RunOnce();

        Assert.False(ReportExists(browser.ReportId));
        Assert.False(ReportExists(service.ReportId));
        Assert.True(ReportExists(fresh.ReportId));
        Assert.True(Directory.Exists(_storage.ReportDirectory(fresh.ReportId)));
    }

    [Fact]
    public void Data_is_deleted_slightly_before_the_limit_so_it_is_never_kept_longer_than_promised()
    {
        var interval = TimeSpan.FromHours(1);
        var justUnder = AddReport("user", TimeSpan.FromDays(7) - TimeSpan.FromMinutes(30)); // would be 7 days old at the next run
        var comfortable = AddReport("user", TimeSpan.FromDays(7) - TimeSpan.FromHours(2));

        Service(interval: interval).RunOnce();

        Assert.False(ReportExists(justUnder.ReportId));
        Assert.True(ReportExists(comfortable.ReportId));
    }

    [Fact]
    public void Job_records_are_deleted_after_the_retention_period_but_waiting_and_running_jobs_stay()
    {
        string done = AddReport("user", TimeSpan.FromDays(8)).JobId;
        string failed = AddJob("user", JobStatus.Failed, TimeSpan.FromDays(9));
        string cancelled = AddJob("user", JobStatus.Cancelled, TimeSpan.FromDays(9));
        string expired = AddJob("user", JobStatus.Expired, TimeSpan.FromDays(9));
        string queued = AddJob("user", JobStatus.Queued, TimeSpan.FromDays(9));
        string running = AddJob("user", JobStatus.Running, TimeSpan.FromDays(9));
        string recent = AddJob("user", JobStatus.Failed, TimeSpan.FromDays(2));

        Service().RunOnce();

        var remaining = _store.ListForUser("user", null, 100).Select(j => j.Id).ToHashSet();
        Assert.DoesNotContain(done, remaining);
        Assert.DoesNotContain(failed, remaining);
        Assert.DoesNotContain(cancelled, remaining);
        Assert.DoesNotContain(expired, remaining);
        Assert.Contains(queued, remaining);
        Assert.Contains(running, remaining);
        Assert.Contains(recent, remaining);
    }

    [Fact]
    public void A_newer_job_for_a_deleted_report_becomes_expired()
    {
        // Same file uploaded again on day 6: its job is young, the shared report is 8 days old.
        var (reportId, _) = AddReport("first", TimeSpan.FromDays(8));
        string later = AddJob("second", JobStatus.Done, TimeSpan.FromDays(2), reportId);

        Service().RunOnce();

        var job = Assert.Single(_store.ListForUser("second", null, 10));
        Assert.Equal(later, job.Id);
        Assert.Equal(JobStatus.Expired, job.Status);
    }

    [Fact]
    public void Oldest_reports_are_deleted_early_when_the_storage_quota_is_exceeded()
    {
        var oldest = AddReport("user", TimeSpan.FromDays(3), sizeMb: 40);
        var middle = AddReport("user", TimeSpan.FromDays(2), sizeMb: 40);
        var newest = AddReport("user", TimeSpan.FromDays(1), sizeMb: 40);

        Service(maxReportsBytes: 100 * MB).RunOnce(); // 120 MB used -> down to <= 90 MB

        Assert.False(ReportExists(oldest.ReportId));
        Assert.True(ReportExists(middle.ReportId));
        Assert.True(ReportExists(newest.ReportId));
        Assert.Equal(80 * MB, _store.GetReportsBytes());
    }

    [Fact]
    public void Orphaned_files_older_than_an_hour_are_removed()
    {
        Directory.CreateDirectory(_storage.UploadsDirectory);
        string orphanUpload = Path.Combine(_storage.UploadsDirectory, "leftover.zevtc");
        string freshUpload = Path.Combine(_storage.UploadsDirectory, "in-progress.zevtc");
        File.WriteAllText(orphanUpload, "x");
        File.WriteAllText(freshUpload, "x");
        string orphanReport = _storage.ReportDirectory("ZZZZZZZZZZ");
        Directory.CreateDirectory(orphanReport);
        var kept = AddReport("user", TimeSpan.FromDays(3));

        var old = _time.GetUtcNow().UtcDateTime.AddHours(-2);
        File.SetLastWriteTimeUtc(orphanUpload, old);
        Directory.SetLastWriteTimeUtc(orphanReport, old);
        Directory.SetLastWriteTimeUtc(_storage.ReportDirectory(kept.ReportId), old);
        File.SetLastWriteTimeUtc(freshUpload, _time.GetUtcNow().UtcDateTime);

        Service().RunOnce();

        Assert.False(File.Exists(orphanUpload));
        Assert.True(File.Exists(freshUpload));
        Assert.False(Directory.Exists(orphanReport));
        Assert.True(ReportExists(kept.ReportId));
    }

    [Fact]
    public void Finished_jobs_show_when_their_report_expires()
    {
        var (_, jobId) = AddReport("user", TimeSpan.FromDays(2));
        var reports = new ReportsOptions { ValidDays = 7 };

        var dto = Breakbar.Api.Jobs.JobDto.From(_store.GetView(jobId)!, null, reports);

        Assert.Equal(_time.GetUtcNow().AddDays(-2).AddDays(7).ToUnixTimeMilliseconds(), dto.ExpiresAt!.Value.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void The_default_retention_is_seven_days()
    {
        Assert.Equal(7, new RetentionOptions().ReportDays);
        Assert.True(new RetentionOptions().DeleteAfter < TimeSpan.FromDays(7));
    }
}
