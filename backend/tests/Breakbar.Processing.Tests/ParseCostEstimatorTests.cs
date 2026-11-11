using System.IO.Compression;
using Breakbar.Processing.Queue;

namespace Breakbar.Processing.Tests;

public sealed class ParseCostEstimatorTests : IDisposable
{
    private const long MB = 1024L * 1024L;
    private readonly string _dir = Directory.CreateTempSubdirectory("breakbar-estimator-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Estimate_uses_base_plus_factor_times_evtc_size()
    {
        var estimator = new ParseCostEstimator(new ParseQueueOptions());

        // Benchmark: a 22.6 MB EVTC peaked at ~976 MB on the VPS; the estimate must stay above that.
        long estimate = estimator.EstimateMemoryBytes((long)(22.6 * MB));

        Assert.Equal(150 * MB + (long)(42 * (long)(22.6 * MB)), estimate);
        Assert.True(estimate > 976 * MB);
    }

    [Fact]
    public void Reads_uncompressed_size_from_zevtc_without_extracting()
    {
        string path = Path.Combine(_dir, "log.zevtc");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry("log").Open())
        {
            entry.Write(new byte[123_456]);
        }

        Assert.Equal(123_456, ParseCostEstimator.GetUncompressedEvtcBytes(path));
    }

    [Fact]
    public void Plain_evtc_size_is_the_file_size()
    {
        string path = Path.Combine(_dir, "log.evtc");
        File.WriteAllBytes(path, new byte[4_096]);

        Assert.Equal(4_096, ParseCostEstimator.GetUncompressedEvtcBytes(path));
    }

    [Fact]
    public void Archive_with_more_than_one_entry_is_rejected()
    {
        string path = Path.Combine(_dir, "two.zevtc");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("a");
            zip.CreateEntry("b");
        }

        Assert.Throws<InvalidDataException>(() => ParseCostEstimator.GetUncompressedEvtcBytes(path));
    }
}
