namespace Breakbar.Processing.EliteInsights;

/// <summary>Parses one log into a report. Implemented by <see cref="EliteInsightsAdapter"/>; faked in tests.</summary>
public interface IEliteInsightsParser
{
    EliteInsightsResult Parse(string logPath, string outputDirectory, CancellationToken cancellationToken = default);
}
