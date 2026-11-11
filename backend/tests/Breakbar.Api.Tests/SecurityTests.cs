using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Breakbar.Api.Jobs;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Breakbar.Api.Tests;

/// <summary>Report origin separation, zip bomb protection and upload capacity limits (docs/PLAN.md 4b).</summary>
public sealed class SecurityTests
{
    private const string ApiBase = "http://logs.breakbar.test";
    private const string ReportsBase = "http://reports.breakbar.test";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpClient Client(ApiFactory factory, string baseAddress)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(baseAddress), AllowAutoRedirect = false });

    private static ApiFactory SeparatedOrigins(Dictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string> { ["Reports:BaseUrl"] = ReportsBase };
        foreach (var (k, v) in extra ?? [])
        {
            settings[k] = v;
        }
        return new ApiFactory(settings: settings);
    }

    // ---- Report origin -------------------------------------------------------------------------

    [Fact]
    public async Task Reports_host_serves_no_api()
    {
        await using var factory = SeparatedOrigins();
        var reportsHost = Client(factory, ReportsBase);

        Assert.Equal(HttpStatusCode.NotFound, (await reportsHost.GetAsync("/api/jobs", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reportsHost.GetAsync("/api/health", Ct)).StatusCode);
    }

    [Fact]
    public async Task Api_host_redirects_report_links_and_serves_no_assets()
    {
        await using var factory = SeparatedOrigins();
        var apiHost = Client(factory, ApiBase);

        var response = await apiHost.GetAsync("/r/AbCdEfGhIj", Ct);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"{ReportsBase}/r/AbCdEfGhIj", response.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await apiHost.GetAsync("/ei-assets/EliteInsights-3.32.0.0.js", Ct)).StatusCode);
    }

    [Fact]
    public async Task Finished_report_lives_on_the_reports_origin_with_hardening_headers()
    {
        string log = TestLogs.RequireSmallest();
        await using var factory = SeparatedOrigins();
        var apiHost = Client(factory, ApiBase);

        var queued = await (await apiHost.UploadAsync(log)).Content.ReadFromJsonAsync<JobDto>(Ct);
        var job = await apiHost.WaitUntilFinishedAsync(queued!.Id);

        Assert.StartsWith($"{ReportsBase}/r/", job.ReportUrl, StringComparison.Ordinal);
        var report = await Client(factory, ReportsBase).GetAsync(job.ReportUrl, Ct);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Equal("no-referrer", report.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("noindex", report.Headers.GetValues("X-Robots-Tag").Single(), StringComparison.Ordinal);
        Assert.False(report.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(report.Headers.Contains("Set-Cookie"));
    }

    // ---- Zip bombs and invalid archives --------------------------------------------------------

    [Fact]
    public async Task Archive_with_forged_size_information_is_not_accepted_as_small_log()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();
        // 5 MB of highly compressible data, but the zip headers claim 100 bytes.
        byte[] bomb = ForgeUncompressedSize(Zip(EvtcBytes(5 * 1024 * 1024)), claimedSize: 100);

        var response = await client.PostAsync("/api/uploads?name=bomb.zevtc", new ByteArrayContent(bomb), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Archive_that_decompresses_beyond_the_limit_is_rejected()
    {
        await using var factory = new ApiFactory(settings: new() { ["Uploads:MaxEvtcBytes"] = "1000000" });
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/uploads?name=big.zevtc", new ByteArrayContent(Zip(EvtcBytes(2_000_000))), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Archive_without_evtc_content_is_rejected()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/uploads?name=x.zevtc", new ByteArrayContent(Zip(new byte[1000])), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ---- Capacity limits (no workers, so uploads stay queued) ----------------------------------

    [Fact]
    public async Task Waiting_jobs_per_user_are_limited()
    {
        await using var factory = new ApiFactory(settings: new() { ["Queue:MaxParallel"] = "0", ["Uploads:MaxQueuedJobsPerUser"] = "2" });
        var user = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Accepted, (await UploadSynthetic(user, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await UploadSynthetic(user, 2)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await UploadSynthetic(user, 3)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await UploadSynthetic(factory.CreateClient(), 4)).StatusCode);
    }

    [Fact]
    public async Task Waiting_jobs_are_limited_in_total_even_with_new_identities()
    {
        await using var factory = new ApiFactory(settings: new() { ["Queue:MaxParallel"] = "0", ["Uploads:MaxQueuedJobsTotal"] = "2" });

        Assert.Equal(HttpStatusCode.Accepted, (await UploadSynthetic(factory.CreateClient(), 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await UploadSynthetic(factory.CreateClient(), 2)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await UploadSynthetic(factory.CreateClient(), 3)).StatusCode);
    }

    [Fact]
    public async Task Uploads_are_refused_when_the_disk_is_almost_full()
    {
        await using var factory = new ApiFactory(settings: new() { ["Uploads:MinFreeDiskBytes"] = long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        var response = await UploadSynthetic(factory.CreateClient(), 1);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string uploads = Path.Combine(factory.DataDirectory, "uploads");
        Assert.True(!Directory.Exists(uploads) || !Directory.EnumerateFiles(uploads).Any());
    }

    // ---- Helpers -------------------------------------------------------------------------------

    /// <summary>Uploads a minimal valid-looking .evtc (magic bytes + filler); unique per <paramref name="n"/>.</summary>
    private static Task<HttpResponseMessage> UploadSynthetic(HttpClient client, int n)
    {
        byte[] data = EvtcBytes(64);
        data[10] = (byte)n;
        return client.PostAsync($"/api/uploads?name=synthetic-{n}.evtc", new ByteArrayContent(data), Ct);
    }

    private static byte[] EvtcBytes(int length)
    {
        byte[] data = new byte[length];
        "EVTC"u8.CopyTo(data);
        return data;
    }

    private static byte[] Zip(byte[] content)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = zip.CreateEntry("log", CompressionLevel.Optimal).Open())
        {
            entry.Write(content);
        }
        return buffer.ToArray();
    }

    /// <summary>Rewrites the uncompressed size in the local file header and the central directory, like a zip bomb would.</summary>
    private static byte[] ForgeUncompressedSize(byte[] zip, uint claimedSize)
    {
        byte[] result = (byte[])zip.Clone();
        for (int i = 0; i + 4 <= result.Length; i++)
        {
            uint signature = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(i));
            if (signature == 0x04034b50) // local file header: uncompressed size at +22
            {
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i + 22), claimedSize);
            }
            else if (signature == 0x02014b50) // central directory header: uncompressed size at +24
            {
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i + 24), claimedSize);
            }
        }
        return result;
    }
}
