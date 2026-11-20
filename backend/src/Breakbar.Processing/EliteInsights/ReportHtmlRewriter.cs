using System.Net;

namespace Breakbar.Processing.EliteInsights;

/// <summary>
/// Post-processes the HTML that Elite Insights generates (docs/PLAN.md 4d): removes third-party requests that are
/// not needed and adds the breakbar.cc tag to the footer. Each step targets one known element of EI's template.
/// If an Elite Insights update changes the template, a step silently does nothing; the report tests
/// ("no Google hosts", "footer tag") then fail and show what to adjust.
/// </summary>
public static class ReportHtmlRewriter
{
    /// <summary>Path below the assets URL where the self-hosted Open Sans CSS lives.</summary>
    public const string OpenSansCssPath = "fonts/open-sans.css";

    public const string SiteName = "breakbar.cc";
    private const string EliteInsightsUrl = "https://github.com/baaron4/GW2-Elite-Insights-Parser";

    /// <summary>EI's template loads Open Sans from Google Fonts unconditionally in the head.</summary>
    private const string GoogleFontsLink = "<link href=\"https://fonts.googleapis.com/css?family=Open+Sans\" rel=\"stylesheet\">";

    /// <summary>Last line of EI's footer ("Recorded by: name - account"), a Vue template expression and therefore unique.</summary>
    private const string RecordedByMarker = "Recorded by: {{ getLogData().recordedBy }}";

    /// <param name="html">Complete report HTML as written by EI.</param>
    /// <param name="assetsBaseUrl">Public URL prefix of the EI assets, e.g. "https://reports.breakbar.cc/ei-assets".</param>
    /// <param name="siteUrl">Link target of the footer tag, e.g. "https://breakbar.cc"; null adds no tag.</param>
    public static string Rewrite(string html, string assetsBaseUrl, string? siteUrl = null)
    {
        html = ReplaceFirst(html, GoogleFontsLink, $"<link href=\"{assetsBaseUrl.TrimEnd('/')}/{OpenSansCssPath}\" rel=\"stylesheet\">");
        return string.IsNullOrWhiteSpace(siteUrl) ? html : AddFooterTag(html, siteUrl);
    }

    /// <summary>
    /// Appends "· Generated with Elite Insights · breakbar.cc" to EI's "Recorded by" line. It stays on that one
    /// line (wrapping only on narrow screens), so the footer does not get taller.
    /// </summary>
    private static string AddFooterTag(string html, string siteUrl)
    {
        int marker = html.IndexOf(RecordedByMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return html;
        }
        const string close = "</div>";
        int end = html.IndexOf(close, marker, StringComparison.Ordinal);
        if (end < 0)
        {
            return html;
        }
        // Inserted just before the closing tag, i.e. inside the same footer_bottom element.
        string tag =
            $" · Generated with <a href=\"{EliteInsightsUrl}\" target=\"_blank\" rel=\"noopener noreferrer\">Elite Insights</a>" +
            $" · <a href=\"{WebUtility.HtmlEncode(siteUrl)}\" target=\"_blank\" rel=\"noopener noreferrer\">{SiteName}</a>";
        return html.Insert(end, tag);
    }

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
