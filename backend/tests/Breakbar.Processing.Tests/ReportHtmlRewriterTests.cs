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

    [Fact]
    public void Html_without_the_link_is_returned_unchanged()
    {
        const string html = "<head></head><body>nothing to do</body>";

        Assert.Equal(html, ReportHtmlRewriter.Rewrite(html, Base));
    }
}
