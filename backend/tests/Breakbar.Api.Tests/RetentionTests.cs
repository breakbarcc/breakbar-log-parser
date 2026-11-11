using Breakbar.Api.Jobs;
using Breakbar.Api.Options;
using Breakbar.Api.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Breakbar.Api.Tests;

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

    private RetentionService Service(long maxReportsBytes = long.MaxValue, int serviceDays = 7)
        => new(_store, _storage, new RetentionOptions { MaxReportsBytes = maxReportsBytes, ServiceClientReportDays = serviceDays },
            _time, NullLogger<RetentionService>.Instance);

    /// <summary>A finished report of <paramref name="sizeMb"/> MB, created <paramref name="daysAgo"/> days ago, with one done job.</summary>
    private string AddReport(string user, int daysAgo, long sizeMb)
    {
        string id = ShortId.New();
        long created = _time.GetUtcNow().AddDays(-daysAgo).ToUnixTimeMilliseconds();
        _store.InsertReportOrGetExisting(new ReportRecord
        {
            Id = id,
            Sha256 = Guid.NewGuid().ToString("N"),
            EiVersion = "3.32.0.0",
            HtmlBytes = sizeMb * MB,
            JsonBytes = 0,
            CreatedAt = created,
        });
        _store.Insert(new JobRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = user,
            FileName = "x.zevtc",
            UploadPath = "",
            Sha256 = "s",
            Status = JobStatus.Done,
            ReportId = id,
            CreatedAt = created,
        });
        Directory.CreateDirectory(_storage.ReportDirectory(id));
        File.WriteAllText(Path.Combine(_storage.ReportDirectory(id), "report.html"), "<html>");
        return id;
    }

    private bool Exists(string reportId) => _store.ReportExists(reportId) && Directory.Exists(_storage.ReportDirectory(reportId));

    [Fact]
    public void Oldest_reports_are_deleted_when_the_quota_is_exceeded()
    {
        string oldest = AddReport("user", daysAgo: 30, sizeMb: 40);
        string middle = AddReport("user", daysAgo: 20, sizeMb: 40);
        string newest = AddReport("user", daysAgo: 1, sizeMb: 40);

        Service(maxReportsBytes: 100 * MB).RunOnce(); // 120 MB used -> down to <= 90 MB

        Assert.False(Exists(oldest));
        Assert.True(Exists(middle));
        Assert.True(Exists(newest));
        Assert.Equal(80 * MB, _store.GetReportsBytes());
    }

    [Fact]
    public void Jobs_of_deleted_reports_become_expired()
    {
        string report = AddReport("user", daysAgo: 30, sizeMb: 200);

        Service(maxReportsBytes: 100 * MB).RunOnce();

        var job = Assert.Single(_store.ListForUser("user", null, 10));
        Assert.Equal(report, job.ReportId);
        Assert.Equal(JobStatus.Expired, job.Status);
    }

    [Fact]
    public void Service_client_reports_expire_after_the_configured_days_unless_a_browser_user_has_them()
    {
        string voidlogOld = AddReport("svc:voidlog", daysAgo: 8, sizeMb: 1);
        string voidlogNew = AddReport("svc:voidlog", daysAgo: 2, sizeMb: 1);
        string userOld = AddReport("user", daysAgo: 30, sizeMb: 1);

        Service(serviceDays: 7).RunOnce();

        Assert.False(Exists(voidlogOld));
        Assert.True(Exists(voidlogNew));
        Assert.True(Exists(userOld));
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
        string kept = AddReport("user", daysAgo: 3, sizeMb: 1);

        var old = _time.GetUtcNow().UtcDateTime.AddHours(-2);
        File.SetLastWriteTimeUtc(orphanUpload, old);
        Directory.SetLastWriteTimeUtc(orphanReport, old);
        Directory.SetLastWriteTimeUtc(_storage.ReportDirectory(kept), old);
        File.SetLastWriteTimeUtc(freshUpload, _time.GetUtcNow().UtcDateTime);

        Service().RunOnce();

        Assert.False(File.Exists(orphanUpload));
        Assert.True(File.Exists(freshUpload));
        Assert.False(Directory.Exists(orphanReport));
        Assert.True(Exists(kept));
    }
}
