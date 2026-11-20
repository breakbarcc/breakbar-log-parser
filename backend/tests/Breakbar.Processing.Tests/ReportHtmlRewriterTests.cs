using Breakbar.Processing.EliteInsights;

namespace Breakbar.Processing.Tests;

public sealed class ReportHtmlRewriterTests
{
    private const string GoogleLink = "<link href=\"https://fonts.googleapis.com/css?family=Open+Sans\" rel=\"stylesheet\">";
    private const string Base = "https://reports.example.test/ei-assets";

    [Fact]
    public void Google_fonts_link_is_replaced_by_the_self_hosted_stylesheet()
    {
        string html = $"<head>{GoogleLink}</head><body></body>";

        string result = ReportHtmlRewriter.Rewrite(html, Base);

        Assert.Equal($"<head><link href=\"{Base}/fonts/open-sans.css\" rel=\"stylesheet\"></head><body></body>", result);
    }

    [Fact]
    public void Trailing_slash_of_the_base_url_does_not_double_up()
    {
        string result = ReportHtmlRewriter.Rewrite(GoogleLink, Base + "/");

        Assert.Contains($"{Base}/fonts/open-sans.css", result, StringComparison.Ordinal);
        Assert.DoesNotContain("//fonts", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_first_occurrence_is_replaced_so_log_data_is_never_altered()
    {
        // A player can put any text into a character name; the same string later in the page is data.
        string html = $"<head>{GoogleLink}</head><script>const _logData = 'name {GoogleLink}';</script>";

        string result = ReportHtmlRewriter.Rewrite(html, Base);

        Assert.Contains($"'name {GoogleLink}'", result, StringComparison.Ordinal);
        Assert.StartsWith($"<head><link href=\"{Base}/fonts/open-sans.css\"", result, StringComparison.Ordinal);
    }

    private const string Footer =
        "<div class=\"footer_bottom\">\n                Recorded by: {{ getLogData().recordedBy }} - {{ getLogData().recordedAccountBy }}\n            </div>";

    [Fact]
    public void Footer_tag_is_added_below_the_recorded_by_line()
    {
        string html = $"<div class=\"footer\">{Footer}</div>";

        string result = ReportHtmlRewriter.Rewrite(html, Base, "https://logs.example.test");

        string tag = "<div class=\"footer_bottom\">Generated with <a href=\"https://github.com/baaron4/GW2-Elite-Insights-Parser\" target=\"_blank\" rel=\"noopener noreferrer\">Elite Insights</a>.<br>" +
                     "<a href=\"https://logs.example.test\" target=\"_blank\" rel=\"noopener noreferrer\">breakbar.cc</a></div>";
        Assert.Equal($"<div class=\"footer\">{Footer}\n            {tag}</div>", result);
    }

    [Fact]
    public void Footer_tag_url_is_html_encoded_and_log_data_after_the_footer_is_not_touched()
    {
        string html = $"{Footer}<script>const _logData = 'Recorded by: {{{{ getLogData().recordedBy }}}} </div>';</script>";

        string result = ReportHtmlRewriter.Rewrite(html, Base, "https://logs.example.test/?a=1&b=\"2\"");

        Assert.Contains("href=\"https://logs.example.test/?a=1&amp;b=&quot;2&quot;\"", result, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result, "breakbar\\.cc</a>"));
        Assert.EndsWith("</div>';</script>", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_footer_tag_without_a_site_url(string? siteUrl)
    {
        string html = $"<div class=\"footer\">{Footer}</div>";

        Assert.Equal(html, ReportHtmlRewriter.Rewrite(html, Base, siteUrl));
    }

    [Fact]
    public void Html_without_the_link_is_returned_unchanged()
    {
        const string html = "<head></head><body>nothing to do</body>";

        Assert.Equal(html, ReportHtmlRewriter.Rewrite(html, Base));
    }
}
