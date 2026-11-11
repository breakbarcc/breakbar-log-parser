namespace Breakbar.Api.Options;

/// <summary>File system layout below one data directory (bind mount on the VPS, see docs/PLAN.md 6b).</summary>
public sealed class StorageOptions
{
    /// <summary>Root data directory; relative paths are resolved against the content root.</summary>
    public string DataDirectory { get; set; } = "data";

    public string DatabasePath => Path.Combine(DataDirectory, "breakbar.db");
    public string UploadsDirectory => Path.Combine(DataDirectory, "uploads");
    public string ReportsDirectory => Path.Combine(DataDirectory, "reports");
    public string AssetsDirectory => Path.Combine(DataDirectory, "ei-assets");

    public string ReportDirectory(string reportId) => Path.Combine(ReportsDirectory, reportId);
}

public sealed class UploadOptions
{
    private const long MB = 1024L * 1024L;

    /// <summary>Largest accepted upload (compressed file as sent).</summary>
    public long MaxUploadBytes { get; set; } = 200 * MB;

    /// <summary>Largest accepted uncompressed EVTC (EI's own limit is 400 MB).</summary>
    public long MaxEvtcBytes { get; set; } = 400 * MB;

    /// <summary>Uploads per minute and client IP; service clients are not limited.</summary>
    public int UploadsPerMinutePerIp { get; set; } = 120;

    /// <summary>Waiting jobs per user (browser identity); service clients are not limited.</summary>
    public int MaxQueuedJobsPerUser { get; set; } = 300;

    /// <summary>Waiting jobs across all users; protects disk and memory when identities are rotated.</summary>
    public int MaxQueuedJobsTotal { get; set; } = 3000;

    /// <summary>Uploads are refused while less than this is free on the data disk.</summary>
    public long MinFreeDiskBytes { get; set; } = 5L * 1024 * MB;
}

/// <summary>
/// Reports are served from their own origin (e.g. https://reports.breakbar.cc) so that report pages, which run
/// Elite Insights' and third-party JavaScript, cannot read the API with the visitor's cookie (docs/PLAN.md 4b).
/// Without <see cref="BaseUrl"/> (local development) reports are served from the same origin.
/// </summary>
public sealed class ReportsOptions
{
    public string? BaseUrl { get; set; }

    /// <summary>Host name of <see cref="BaseUrl"/>, or null when reports share the API origin.</summary>
    public string? Host => string.IsNullOrWhiteSpace(BaseUrl) ? null : new Uri(BaseUrl).Host;

    public string ReportUrl(string reportId) => $"{BaseUrl?.TrimEnd('/')}/r/{reportId}";
}

/// <summary>Trusted service clients (e.g. voidlog) authenticated by the X-Api-Key header. Key: client name, value: API key.</summary>
public sealed class ServiceClientOptions
{
    public Dictionary<string, string> ApiKeys { get; set; } = [];
}
