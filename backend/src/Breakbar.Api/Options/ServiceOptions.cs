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
}

/// <summary>Trusted service clients (e.g. voidlog) authenticated by the X-Api-Key header. Key: client name, value: API key.</summary>
public sealed class ServiceClientOptions
{
    public Dictionary<string, string> ApiKeys { get; set; } = [];
}
