using System.Net;
using System.Net.Http.Json;
using Breakbar.Api.Jobs;
using Breakbar.Api.Options;
using Breakbar.Api.Persistence;

namespace Breakbar.Api.Tests;

public sealed class UploadApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_reports_Elite_Insights_version_and_queue()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        string body = await client.GetStringAsync("/api/health", Ct);

        Assert.Contains("\"eliteInsights\":\"3.", body, StringComparison.Ordinal);
        Assert.Contains("\"queue\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_without_log_extension_is_rejected()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/uploads?name=notes.txt", new StringContent("hello"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_log_content_is_rejected_and_not_stored()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/uploads?name=fake.zevtc", new StringContent("not a zip"), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(await client.GetFromJsonAsync<List<JobDto>>("/api/jobs", Ct) ?? []);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(factory.DataDirectory, "uploads")));
    }

    [Fact]
    public async Task Unknown_api_key_is_rejected()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");

        var response = await client.PostAsync("/api/uploads?name=a.zevtc", new StringContent("x"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Uploaded_log_is_parsed_and_report_is_served()
    {
        string log = TestLogs.RequireSmallest();
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.UploadAsync(log, batch: "raid-1");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var queued = await response.Content.ReadFromJsonAsync<JobDto>(Ct);

        var job = await client.WaitUntilFinishedAsync(queued!.Id);
        Assert.Equal("done", job.Status);
        Assert.Equal("raid-1", job.BatchId);
        Assert.False(string.IsNullOrEmpty(job.EncounterId));

        string html = await client.GetStringAsync(job.ReportUrl, Ct);
        Assert.Contains("/ei-assets/EliteInsights-", html, StringComparison.Ordinal);

        var json = await client.GetAsync(job.JsonUrl, Ct);
        Assert.Equal(HttpStatusCode.OK, json.StatusCode);
        Assert.Contains("gzip", json.Content.Headers.ContentEncoding);

        // Upload is deleted after parsing; the EI assets exist once.
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(factory.DataDirectory, "uploads")));
        string asset = Directory.EnumerateFiles(Path.Combine(factory.DataDirectory, "ei-assets"), "EliteInsights-*.js").First();
        var assetResponse = await client.GetAsync("/ei-assets/" + Path.GetFileName(asset), Ct);
        Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
    }

    [Fact]
    public async Task Same_file_uploaded_again_returns_the_existing_report_immediately()
    {
        string log = TestLogs.RequireSmallest();
        await using var factory = new ApiFactory();
        var first = factory.CreateClient();
        var queued = await (await first.UploadAsync(log)).Content.ReadFromJsonAsync<JobDto>(Ct);
        var done = await first.WaitUntilFinishedAsync(queued!.Id);

        var otherUser = factory.CreateClient();
        var response = await otherUser.UploadAsync(log, name: "copy.zevtc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var duplicate = await response.Content.ReadFromJsonAsync<JobDto>(Ct);
        Assert.Equal("done", duplicate!.Status);
        Assert.Equal(done.ReportId, duplicate.ReportId);
        Assert.Equal("copy.zevtc", duplicate.FileName);
    }

    [Fact]
    public async Task Jobs_are_only_visible_to_their_owner()
    {
        string log = TestLogs.RequireSmallest();
        await using var factory = new ApiFactory();
        var owner = factory.CreateClient();
        var job = await (await owner.UploadAsync(log)).Content.ReadFromJsonAsync<JobDto>(Ct);

        var stranger = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/jobs/{job!.Id}", Ct)).StatusCode);
        Assert.Empty(await stranger.GetFromJsonAsync<List<JobDto>>("/api/jobs", Ct) ?? []);
        Assert.Single(await owner.GetFromJsonAsync<List<JobDto>>("/api/jobs", Ct) ?? []);
    }

    [Fact]
    public async Task Service_client_uploads_with_api_key()
    {
        string log = TestLogs.RequireSmallest();
        await using var factory = new ApiFactory(settings: new() { ["ServiceClients:ApiKeys:voidlog"] = "test-key-123" });
        var voidlog = factory.CreateClient();
        voidlog.DefaultRequestHeaders.Add("X-Api-Key", "test-key-123");

        var response = await voidlog.UploadAsync(log);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await voidlog.WaitUntilFinishedAsync((await response.Content.ReadFromJsonAsync<JobDto>(Ct))!.Id);

        Assert.Equal("done", job.Status);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Events_stream_pushes_job_updates()
    {
        string log = TestLogs.RequireSmallest();
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        await client.GetAsync("/api/jobs", Ct); // obtain the identity cookie first

        using var events = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal("text/event-stream", events.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync(Ct));
        Assert.Equal("event: ready", await reader.ReadLineAsync(Ct));

        await client.UploadAsync(log);

        var statuses = new List<string>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        string? eventType = null;
        while (!statuses.Contains("done") && await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventType = line[7..];
            }
            else if (eventType == "job" && line.StartsWith("data: ", StringComparison.Ordinal))
            {
                statuses.Add(System.Text.Json.JsonDocument.Parse(line[6..]).RootElement.GetProperty("status").GetString()!);
            }
        }

        Assert.Equal(["queued", "running", "done"], statuses);
    }

    [Fact]
    public async Task Pending_job_from_a_previous_run_is_parsed_after_restart()
    {
        string log = TestLogs.RequireSmallest();
        string dataDirectory = Directory.CreateTempSubdirectory("breakbar-api-").FullName;
        const string userId = "0123456789abcdef0123456789abcdef";
        string jobId = Guid.NewGuid().ToString("N");

        // Simulate a job that was waiting when the service stopped.
        var storage = new StorageOptions { DataDirectory = dataDirectory };
        var database = new Database(storage);
        database.Initialize();
        Directory.CreateDirectory(storage.UploadsDirectory);
        string upload = Path.Combine(storage.UploadsDirectory, jobId + ".zevtc");
        File.Copy(log, upload);
        new JobStore(database, TimeProvider.System).Insert(new JobRecord
        {
            Id = jobId,
            UserId = userId,
            FileName = "left-over.zevtc",
            UploadPath = upload,
            Sha256 = "recovered-test",
            FileBytes = new FileInfo(upload).Length,
            EvtcBytes = 1,
            EstimateBytes = 200L * 1024 * 1024,
            Status = JobStatus.Running,
            ReportId = ShortId.New(),
            CreatedAt = 1,
        });
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        await using var factory = new ApiFactory(dataDirectory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"bb_uid={userId}");

        var job = await client.WaitUntilFinishedAsync(jobId);

        Assert.Equal("done", job.Status);
        Assert.False(File.Exists(upload));
    }
}
