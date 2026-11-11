namespace Breakbar.Processing.EliteInsights;

/// <summary>Outcome of parsing one log. On failure only <see cref="Success"/>, <see cref="FailureReason"/> and <see cref="Elapsed"/> are set.</summary>
public sealed record EliteInsightsResult
{
    public required bool Success { get; init; }

    /// <summary>Human readable reason when <see cref="Success"/> is false.</summary>
    public string? FailureReason { get; init; }

    /// <summary>True when the file itself is the problem (not EVTC, too short, too big ...), false for parser bugs.</summary>
    public bool IsContentIssue { get; init; }

    /// <summary>Encounter/instance name, e.g. "Gorseval the Multifarious".</summary>
    public string? LogName { get; init; }

    /// <summary>Short EI id of the encounter, e.g. "gors"; used in file names.</summary>
    public string? EncounterId { get; init; }

    /// <summary>Kill / success of the main phase.</summary>
    public bool? EncounterSuccess { get; init; }

    public long? DurationMs { get; init; }

    public string? HtmlPath { get; init; }

    /// <summary>EI JSON export, gzip compressed.</summary>
    public string? JsonGzPath { get; init; }

    public TimeSpan Elapsed { get; init; }
}
