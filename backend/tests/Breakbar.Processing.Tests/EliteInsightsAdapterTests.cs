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
            SiteUrl = "https://logs.example.test",
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
    public void Report_does_not_load_google_fonts_and_the_self_hosted_font_is_available()
    {
        string log = TestLogs.RequireSmallest();

        var result = _adapter.Parse(log, Path.Combine(_workDir, "fonts-report"), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.FailureReason);
        string html = File.ReadAllText(result.HtmlPath!);
        // If this fails after an Elite Insights update, its template changed: adjust ReportHtmlRewriter (docs/PLAN.md 4d).
        Assert.DoesNotContain("fonts.googleapis.com", html, StringComparison.Ordinal);
        Assert.DoesNotContain("fonts.gstatic.com", html, StringComparison.Ordinal);
        Assert.Contains($"{AssetsBaseUrl}/fonts/open-sans.css", html, StringComparison.Ordinal);

        // The stylesheet and every font file it refers to are published next to EI's JS/CSS.
        string fonts = Path.Combine(_workDir, "ei-assets", "fonts");
        string css = File.ReadAllText(Path.Combine(fonts, "open-sans.css"));
        var files = System.Text.RegularExpressions.Regex.Matches(css, @"url\(([^)]+\.woff2)\)").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.True(File.Exists(Path.Combine(fonts, f)), $"missing {f}"));
        Assert.True(File.Exists(Path.Combine(fonts, "OFL.txt")), "font license must be shipped with the fonts");
    }

    [Fact]
    public void Report_footer_carries_the_breakbar_tag()
    {
        string log = TestLogs.RequireSmallest();

        var result = _adapter.Parse(log, Path.Combine(_workDir, "tag-report"), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.FailureReason);
        string html = File.ReadAllText(result.HtmlPath!);
        // If this fails after an Elite Insights update, its footer changed: adjust ReportHtmlRewriter (docs/PLAN.md 4d).
        Assert.Contains("Generated with <a href=\"https://github.com/baaron4/GW2-Elite-Insights-Parser\"", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://logs.example.test\" target=\"_blank\" rel=\"noopener noreferrer\">breakbar.cc</a>", html, StringComparison.Ordinal);
        // The tag is part of the "Recorded by" line (same element), so the footer keeps its height.
        int recorded = html.IndexOf("Recorded by:", StringComparison.Ordinal);
        int tag = html.IndexOf(">breakbar.cc</a>", StringComparison.Ordinal);
        int lineEnd = html.IndexOf("</div>", recorded, StringComparison.Ordinal);
        Assert.True(recorded < tag && tag < lineEnd, "tag must sit between 'Recorded by' and the end of its element");
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
