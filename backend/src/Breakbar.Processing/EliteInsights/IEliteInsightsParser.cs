namespace Breakbar.Processing.EliteInsights;

/// <summary>Parses one log into a report. Implemented by <see cref="EliteInsightsAdapter"/>; faked in tests.</summary>
public interface IEliteInsightsParser
{
    /// <param name="anonymizePlayers">Replace character and account names in the report (EI option).</param>
    EliteInsightsResult Parse(string logPath, string outputDirectory, bool anonymizePlayers, CancellationToken cancellationToken = default);
}
