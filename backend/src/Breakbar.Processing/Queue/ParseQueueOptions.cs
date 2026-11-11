namespace Breakbar.Processing.Queue;

/// <summary>
/// Limits for <see cref="FairParseQueue"/>. Defaults come from the VPS benchmark (docs/PLAN.md, sections 1c and 3):
/// 2 vCPU / 4 GB, two single-threaded parses in parallel, RAM ≈ 150 MB + 42 × uncompressed EVTC size.
/// </summary>
public sealed class ParseQueueOptions
{
    private const long MB = 1024L * 1024L;

    /// <summary>Maximum number of logs parsed at the same time (one per worker).</summary>
    public int MaxParallel { get; init; } = 2;

    /// <summary>Sum of <see cref="ParseJob.EstimatedMemoryBytes"/> allowed across running jobs.</summary>
    public long MemoryBudgetBytes { get; init; } = 2400 * MB;

    /// <summary>Jobs estimated above this run alone (no other job starts while they run).</summary>
    public long ExclusiveThresholdBytes { get; init; } = 1200 * MB;

    /// <summary>Jobs estimated above this are rejected on enqueue.</summary>
    public long MaxJobMemoryBytes { get; init; } = 2400 * MB;

    /// <summary>Fixed part of the memory estimate per job.</summary>
    public long EstimateBaseBytes { get; init; } = 150 * MB;

    /// <summary>Memory per byte of uncompressed EVTC data.</summary>
    public double EstimateBytesPerEvtcByte { get; init; } = 42;
}
