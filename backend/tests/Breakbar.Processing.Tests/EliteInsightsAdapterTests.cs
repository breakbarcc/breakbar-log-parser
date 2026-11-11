using System.IO.Compression;
using System.Text.Json;
using Breakbar.Processing.EliteInsights;

namespace Breakbar.Processing.Tests;

public sealed class EliteInsightsAdapterTests : IDisposable
{
    private const string AssetsBaseUrl = "https://logs.example.test/ei-assets";

    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "breakbar-tests", Guid.NewGuid().ToString("N"));
    private readonly EliteInsightsAdapter _adapter;

    public EliteInsightsAdapterTests()
    {
        _adapter = new EliteInsightsAdapter(new EliteInsightsOptions
        {
            AssetsDirectory = Path.Combine(_workDir, "ei-assets"),
            AssetsBaseUrl = AssetsBaseUrl,
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
        {
            Directory.Delete(_workDir, recursive: true);
        }
    }

    [Fact]
    public void Reports_the_bundled_Elite_Insights_version()
    {
        Assert.True(_adapter.EliteInsightsVersion.Major >= 3, $"Unexpected EI version {_adapter.EliteInsightsVersion}");
    }

    [Fact]
    public void Non_evtc_file_fails_as_content_issue_without_throwing()
    {
        Directory.CreateDirectory(_workDir);
        string bogus = Path.Combine(_workDir, "not-a-log.zevtc");
        File.WriteAllText(bogus, "this is not a zip archive");

        var result = _adapter.Parse(bogus, Path.Combine(_workDir, "out"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureReason));
        Assert.Null(result.HtmlPath);
    }

    [Fact]
    public void Parses_real_log_into_html_and_gzipped_json()
    {
        string log = TestLogs.RequireSmallest();
        string outDir = Path.Combine(_workDir, "report");

        var result = _adapter.Parse(log, outDir, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.FailureReason);
        Assert.False(string.IsNullOrEmpty(result.EncounterId));
        Assert.True(result.DurationMs > 0);

        // HTML references EI's JS/CSS through the public base URL, never through local file paths.
        string html = File.ReadAllText(result.HtmlPath!);
        Assert.Contains($"{AssetsBaseUrl}/EliteInsights-{_adapter.EliteInsightsVersion}.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain("file://", html, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_workDir, "ei-assets", $"EliteInsights-{_adapter.EliteInsightsVersion}.js")));

        // JSON export is valid gzip-compressed JSON produced by the same EI version.
        using var gzip = new GZipStream(File.OpenRead(result.JsonGzPath!), CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        Assert.Equal(_adapter.EliteInsightsVersion.ToString(), json.RootElement.GetProperty("eliteInsightsVersion").GetString());
    }

    [Fact]
    public void Cancelled_parse_throws_OperationCanceledException()
    {
        string log = TestLogs.RequireSmallest();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => _adapter.Parse(log, Path.Combine(_workDir, "cancelled"), cts.Token));
    }
}
