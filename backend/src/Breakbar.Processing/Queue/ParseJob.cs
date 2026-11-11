namespace Breakbar.Processing.Queue;

/// <summary>One log waiting to be parsed.</summary>
/// <param name="Id">Job id.</param>
/// <param name="UserId">Fairness key: anonymous user token, or a service client such as voidlog.</param>
/// <param name="LogPath">Uploaded .evtc/.zevtc file.</param>
/// <param name="OutputDirectory">Where the report files are written.</param>
/// <param name="EstimatedMemoryBytes">Expected peak memory while parsing, see <see cref="ParseCostEstimator"/>.</param>
/// <param name="AnonymizePlayers">Replace character and account names in the report.</param>
public sealed record ParseJob(
    Guid Id,
    string UserId,
    string LogPath,
    string OutputDirectory,
    long EstimatedMemoryBytes,
    bool AnonymizePlayers = false);
