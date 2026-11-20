namespace Breakbar.Processing.EliteInsights;

/// <summary>
/// Post-processes the HTML that Elite Insights generates so that report pages do not contact third parties
/// that are not needed (docs/PLAN.md 4d). Each entry replaces one known element of EI's template.
/// If a Elite Insights update changes the template, the replacement silently does nothing; the report test
/// ("no Google hosts in the HTML") then fails and shows what to adjust.
/// </summary>
public static class ReportHtmlRewriter
{
    /// <summary>Path below the assets URL where the self-hosted Open Sans CSS lives.</summary>
    public const string OpenSansCssPath = "fonts/open-sans.css";

    /// <summary>EI's template loads Open Sans from Google Fonts unconditionally in the head.</summary>
    private const string GoogleFontsLink = "<link href=\"https://fonts.googleapis.com/css?family=Open+Sans\" rel=\"stylesheet\">";

    /// <param name="html">Complete report HTML as written by EI.</param>
    /// <param name="assetsBaseUrl">Public URL prefix of the EI assets, e.g. "https://reports.breakbar.cc/ei-assets".</param>
    public static string Rewrite(string html, string assetsBaseUrl)
        => ReplaceFirst(html, GoogleFontsLink, $"<link href=\"{assetsBaseUrl.TrimEnd('/')}/{OpenSansCssPath}\" rel=\"stylesheet\">");

    /// <summary>
    /// Replaces only the first occurrence: the template comes first, log data (player names) later,
    /// and data must never be altered.
    /// </summary>
    private static string ReplaceFirst(string text, string search, string replacement)
    {
        int index = text.IndexOf(search, StringComparison.Ordinal);
        return index < 0 ? text : string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + search.Length));
    }
}
