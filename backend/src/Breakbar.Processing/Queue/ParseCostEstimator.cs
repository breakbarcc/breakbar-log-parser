using System.IO.Compression;

namespace Breakbar.Processing.Queue;

/// <summary>Estimates the peak memory EI needs for a log, from its uncompressed EVTC size.</summary>
public sealed class ParseCostEstimator(ParseQueueOptions options)
{
    public long EstimateMemoryBytes(long uncompressedEvtcBytes)
        => options.EstimateBaseBytes + (long)(options.EstimateBytesPerEvtcByte * uncompressedEvtcBytes);

    /// <summary>
    /// Uncompressed EVTC size of a .evtc file, or of the single entry in a .zevtc/.evtc.zip archive
    /// (read from the zip directory, nothing is decompressed).
    /// </summary>
    /// <exception cref="InvalidDataException">The archive is not a zip with exactly one entry.</exception>
    public static long GetUncompressedEvtcBytes(string logPath)
    {
        if (logPath.EndsWith(".evtc", StringComparison.OrdinalIgnoreCase))
        {
            return new FileInfo(logPath).Length;
        }
        using var archive = ZipFile.OpenRead(logPath);
        if (archive.Entries.Count != 1)
        {
            throw new InvalidDataException($"Expected exactly one entry in the archive, found {archive.Entries.Count}.");
        }
        return archive.Entries[0].Length;
    }

    public long EstimateMemoryBytes(string logPath) => EstimateMemoryBytes(GetUncompressedEvtcBytes(logPath));
}
