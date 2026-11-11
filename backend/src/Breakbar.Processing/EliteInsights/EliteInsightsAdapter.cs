using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using GW2EIBuilders;
using GW2EIEvtcParser;
using GW2EIEvtcParser.ParserHelpers;
using GW2EIGW2API;

namespace Breakbar.Processing.EliteInsights;

/// <summary>
/// The single place where breakbar talks to Elite Insights. When updating the EI submodule,
/// only this file (and its options/result types) should need changes.
/// Thread-safe: one instance is shared by all workers; each call parses one log on the calling thread.
/// </summary>
public sealed class EliteInsightsAdapter : IEliteInsightsParser
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly CultureInfo EiCulture = CultureInfo.GetCultureInfo("en-US");

    private readonly EliteInsightsOptions _options;
    private readonly GW2APIController _apiController;
    private readonly HTMLAssets _htmlAssets;

    public EliteInsightsAdapter(EliteInsightsOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.AssetsDirectory);

        // Loaded once per process: the ~10 MB skill cache and the HTML templates.
        // This is the per-log startup cost the CLI pays on every run (docs/PLAN.md, section 2).
        _apiController = new GW2APIController(
            Path.Combine(options.ApiCacheDirectory, "SkillList.json"),
            Path.Combine(options.ApiCacheDirectory, "SpecList.json"),
            Path.Combine(options.ApiCacheDirectory, "TraitList.json"),
            Path.Combine(options.ApiCacheDirectory, "MapList.json"));
        _htmlAssets = new HTMLAssets();
        EliteInsightsVersion = typeof(GW2EIJSON.JsonLog).Assembly.GetName().Version ?? new Version(0, 0);
    }

    /// <summary>Version of the bundled Elite Insights (e.g. 3.32.0.0); also names the JS/CSS asset files.</summary>
    public Version EliteInsightsVersion { get; }

    /// <summary>
    /// Parses one .evtc/.zevtc/.evtc.zip file and writes "report.html" and "report.json.gz" into <paramref name="outputDirectory"/>.
    /// Never throws for bad input; failures are returned in the result. Cancellation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public EliteInsightsResult Parse(string logPath, string outputDirectory, CancellationToken cancellationToken = default)
        => Parse(logPath, outputDirectory, anonymizePlayers: false, cancellationToken);

    /// <inheritdoc cref="Parse(string, string, CancellationToken)"/>
    /// <param name="anonymizePlayers">Replace character and account names in the report.</param>
    public EliteInsightsResult Parse(string logPath, string outputDirectory, bool anonymizePlayers, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = EiCulture; // EI formats numbers in the HTML/JSON with the current culture
        try
        {
            var controller = new AdapterController(cancellationToken);
            var parser = new EvtcParser(CreateParserSettings(anonymizePlayers), _apiController);

            // Benchmark: multi-threading inside one log gains nothing on the VPS -> always single-threaded.
            var log = parser.ParseLog(controller, new FileInfo(logPath), out var failure, multiThreadAcceleration: false);
            // EI reports cancellation as a parsing failure; surface it as a real cancellation instead.
            cancellationToken.ThrowIfCancellationRequested();
            if (log is null)
            {
                return new EliteInsightsResult
                {
                    Success = false,
                    FailureReason = failure?.Reason ?? "Unknown parser failure",
                    IsContentIssue = failure?.IsEvtcContentIssue ?? false,
                    Elapsed = stopwatch.Elapsed,
                };
            }

            Directory.CreateDirectory(outputDirectory);
            var uploadResults = new UploadResults();
            string htmlPath = Path.Combine(outputDirectory, "report.html");
            string jsonGzPath = Path.Combine(outputDirectory, "report.json.gz");
            try
            {
                WriteHtml(log, htmlPath, uploadResults);
                controller.UpdateProgressWithCancellationCheck("breakbar: HTML written");
                WriteJsonGz(log, jsonGzPath, uploadResults);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new EliteInsightsResult
                {
                    Success = false,
                    FailureReason = "Report generation failed: " + ex.Message,
                    IsContentIssue = false,
                    Elapsed = stopwatch.Elapsed,
                };
            }

            return new EliteInsightsResult
            {
                Success = true,
                LogName = log.LogData.LogName,
                EncounterId = log.LogData.Logic.Extension,
                EncounterSuccess = log.LogData.GetMainPhase(log).Success,
                DurationMs = log.LogData.LogDuration,
                HtmlPath = htmlPath,
                JsonGzPath = jsonGzPath,
                Elapsed = stopwatch.Elapsed,
            };
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }

    private EvtcParserSettings CreateParserSettings(bool anonymizePlayers) => new(_options.TooShortLimitMs, _options.TooBigLimitMb)
    {
        AnonymousPlayers = anonymizePlayers,
        SkipFailedTries = false,
        ComputePhases = true,
        ComputeCombatReplay = _options.ParseCombatReplay,
        ComputeDamageModifiers = true,
        ParseExtensions = true,
        ComputeBuff = true,
        ComputeDamage = true,
        ComputeCast = true,
        ComputeMechanics = true,
        DetailedWvWParse = false, // output size and time grow "exponentially" (EI README)
    };

    private void WriteHtml(GW2EIEvtcParser.ParsedEvtcLog log, string htmlPath, UploadResults uploadResults)
    {
        // ExternalHtmlScriptsPath: EI writes its JS/CSS there once per version.
        // ExternalHtmlScriptsCdn: the HTML references the assets via this URL instead of file:// paths.
        var settings = new HTMLSettings(_options.AssetsDirectory, _options.AssetsBaseUrl)
        {
            ExternalHTMLScripts = true,
            CompressJson = true,
            HTMLLightTheme = false,
        };
        using var stream = new FileStream(htmlPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
        using var writer = new StreamWriter(stream, Utf8NoBom);
        var builder = new HTMLBuilder(log, settings, _htmlAssets, EliteInsightsVersion, uploadResults);
        builder.CreateHTML(writer, _options.AssetsDirectory);
    }

    private void WriteJsonGz(GW2EIEvtcParser.ParsedEvtcLog log, string jsonGzPath, UploadResults uploadResults)
    {
        using var file = new FileStream(jsonGzPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        var builder = new RawFormatBuilder(log, new RawFormatSettings { RawFormatTimelineArrays = true }, EliteInsightsVersion, uploadResults);
        builder.CreateJSON(gzip, indent: false);
    }

    /// <summary>Connects EI's progress/cancellation hooks to a <see cref="CancellationToken"/>.</summary>
    private sealed class AdapterController(CancellationToken cancellationToken) : ParserController
    {
        protected override void ThrowIfCanceled() => cancellationToken.ThrowIfCancellationRequested();
    }
}
