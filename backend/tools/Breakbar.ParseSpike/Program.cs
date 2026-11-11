using System.Diagnostics;
using Breakbar.Processing.EliteInsights;

// Spike: parses logs in-process with the EliteInsightsAdapter and prints timing and memory.
// Usage: dotnet run --project tools/Breakbar.ParseSpike -c Release -- <log or folder> [<log or folder> ...] [--out <dir>] [--rounds <n>]
// With --rounds 2 every log is parsed twice: the second round shows the warm, in-process cost per log.

var inputs = new List<string>();
string outRoot = Path.Combine(Path.GetTempPath(), "breakbar-spike");
int rounds = 1;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out": outRoot = args[++i]; break;
        case "--rounds": rounds = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
        default: inputs.Add(args[i]); break;
    }
}

var logs = inputs
    .SelectMany<string, string>(p => Directory.Exists(p)
        ? Directory.EnumerateFiles(p).Where(IsLog).Order()
        : [p])
    .ToList();
if (logs.Count == 0)
{
    Console.Error.WriteLine("Usage: Breakbar.ParseSpike <log or folder>... [--out <dir>] [--rounds <n>]");
    return 1;
}

var startup = Stopwatch.StartNew();
var adapter = new EliteInsightsAdapter(new EliteInsightsOptions
{
    AssetsDirectory = Path.Combine(outRoot, "ei-assets"),
    AssetsBaseUrl = "/ei-assets",
});
Console.WriteLine($"Elite Insights {adapter.EliteInsightsVersion}, adapter ready in {startup.ElapsedMilliseconds} ms, output: {outRoot}");
Console.WriteLine();
Console.WriteLine($"{"round",5} {"file",-28} {"ok",-5} {"ms",7} {"peakMB",7}  result");

using var process = Process.GetCurrentProcess();
int failures = 0;
for (int round = 1; round <= rounds; round++)
{
    foreach (var log in logs)
    {
        string outDir = Path.Combine(outRoot, "reports", $"r{round}-{Path.GetFileNameWithoutExtension(log)}");
        var result = adapter.Parse(log, outDir);
        process.Refresh();
        string info = result.Success
            ? $"{result.EncounterId} {(result.EncounterSuccess == true ? "kill" : "fail")} ({result.LogName})"
            : $"FAILED: {result.FailureReason}";
        if (!result.Success) failures++;
        Console.WriteLine($"{round,5} {Path.GetFileName(log),-28} {result.Success,-5} {result.Elapsed.TotalMilliseconds,7:F0} {process.PeakWorkingSet64 / 1024 / 1024,7}  {info}");
    }
}
return failures == 0 ? 0 : 2;

static bool IsLog(string path)
{
    string name = path.ToLowerInvariant();
    return name.EndsWith(".evtc", StringComparison.Ordinal)
        || name.EndsWith(".zevtc", StringComparison.Ordinal)
        || name.EndsWith(".evtc.zip", StringComparison.Ordinal);
}
