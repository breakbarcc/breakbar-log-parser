namespace Breakbar.Processing.EliteInsights;

/// <summary>
/// Service-wide Elite Insights settings. Mirrors the production settings validated by the
/// benchmark (docs/PLAN.md, section 1c).
/// </summary>
public sealed class EliteInsightsOptions
{
    /// <summary>
    /// Directory that receives EI's JS/CSS once per EI version (served by Caddy under <see cref="AssetsBaseUrl"/>).
    /// </summary>
    public required string AssetsDirectory { get; init; }

    /// <summary>
    /// Public URL prefix the generated HTML uses for EI's JS/CSS, e.g. "https://logs.breakbar.cc/ei-assets".
    /// Without it EI would write file:// links into the HTML.
    /// </summary>
    public required string AssetsBaseUrl { get; init; }

    /// <summary>Directory holding EI's GW2 API caches (SkillList.json, SpecList.json, MapList.json).</summary>
    public string ApiCacheDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "EliteInsightsContent");

    public bool ParseCombatReplay { get; init; } = true;

    /// <summary>Logs shorter than this (ms) are rejected as "too short". EI default: 2200.</summary>
    public long TooShortLimitMs { get; init; } = 2200;

    /// <summary>Logs bigger than this (MB, uncompressed EVTC) are rejected. EI default: 400.</summary>
    public long TooBigLimitMb { get; init; } = 400;
}
