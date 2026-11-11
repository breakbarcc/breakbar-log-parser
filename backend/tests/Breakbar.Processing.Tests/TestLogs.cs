namespace Breakbar.Processing.Tests;

/// <summary>
/// Locates real arcdps logs for integration tests. Logs are not committed (size, player names);
/// tests that need one are skipped when none is available.
/// Search order: BREAKBAR_TEST_LOG_DIR, then benchmark/logs in the repository.
/// </summary>
internal static class TestLogs
{
    public static string? FindSmallest()
    {
        string? dir = Environment.GetEnvironmentVariable("BREAKBAR_TEST_LOG_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            string? root = FindRepoRoot();
            dir = root is null ? null : Path.Combine(root, "benchmark", "logs");
        }
        if (dir is null || !Directory.Exists(dir))
        {
            return null;
        }
        return new DirectoryInfo(dir).EnumerateFiles()
            .Where(f => f.Name.EndsWith(".zevtc", StringComparison.OrdinalIgnoreCase)
                     || f.Name.EndsWith(".evtc", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Length)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }

    public static string RequireSmallest()
    {
        string? log = FindSmallest();
        Assert.SkipWhen(log is null, "No test log found (set BREAKBAR_TEST_LOG_DIR or put logs into benchmark/logs).");
        return log!;
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, ".gitmodules")))
            {
                return dir.FullName;
            }
        }
        return null;
    }
}
