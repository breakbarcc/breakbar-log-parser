using System.Net.Http.Json;
using Breakbar.Api.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Breakbar.Api.Tests;

/// <summary>Runs the real API in-memory (workers and Elite Insights included) on a fresh temporary data directory.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string> _settings;

    public ApiFactory(string? dataDirectory = null, Dictionary<string, string>? settings = null)
    {
        DataDirectory = dataDirectory ?? Directory.CreateTempSubdirectory("breakbar-api-").FullName;
        _settings = settings ?? [];
    }

    public string DataDirectory { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Storage:DataDirectory", DataDirectory);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); // release the database file before deleting it
        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // temp folder, not critical
        }
    }
}

internal static class ApiTestExtensions
{
    public static Task<HttpResponseMessage> UploadAsync(this HttpClient client, string path, string? name = null, string? batch = null)
    {
        var content = new StreamContent(File.OpenRead(path));
        string url = $"/api/uploads?name={Uri.EscapeDataString(name ?? Path.GetFileName(path))}" + (batch is null ? "" : $"&batch={batch}");
        return client.PostAsync(url, content, TestContext.Current.CancellationToken);
    }

    /// <summary>Polls GET /api/jobs/{id} until the job leaves queued/running.</summary>
    public static async Task<JobDto> WaitUntilFinishedAsync(this HttpClient client, string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            var job = await client.GetFromJsonAsync<JobDto>($"/api/jobs/{jobId}", TestContext.Current.CancellationToken);
            if (job is { Status: not ("queued" or "running") })
            {
                return job;
            }
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException($"Job {jobId} did not finish.");
    }
}

/// <summary>Real arcdps logs are not committed; tests that need one are skipped without it.</summary>
internal static class TestLogs
{
    public static string RequireSmallest()
    {
        string? dir = Environment.GetEnvironmentVariable("BREAKBAR_TEST_LOG_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, ".gitmodules")))
                {
                    dir = Path.Combine(d.FullName, "benchmark", "logs");
                    break;
                }
            }
        }
        string? log = dir is not null && Directory.Exists(dir)
            ? new DirectoryInfo(dir).EnumerateFiles("*.zevtc").OrderBy(f => f.Length).Select(f => f.FullName).FirstOrDefault()
            : null;
        Assert.SkipWhen(log is null, "No test log found (set BREAKBAR_TEST_LOG_DIR or put logs into benchmark/logs).");
        return log!;
    }
}
