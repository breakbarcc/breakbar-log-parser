using System.Net;

namespace Breakbar.Api.Tests;

public sealed class FrontendHostingTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _frontend = Directory.CreateTempSubdirectory("breakbar-frontend-").FullName;

    public FrontendHostingTests()
    {
        File.WriteAllText(Path.Combine(_frontend, "index.html"), "<!doctype html><div id=root>breakbar-spa</div>");
        Directory.CreateDirectory(Path.Combine(_frontend, "assets"));
        File.WriteAllText(Path.Combine(_frontend, "assets", "app-abc123.js"), "console.log(1)");
    }

    public void Dispose() => Directory.Delete(_frontend, recursive: true);

    private ApiFactory Factory() => new(settings: new() { ["Frontend:Directory"] = _frontend });

    [Fact]
    public async Task Client_side_routes_return_the_spa_with_a_strict_content_security_policy()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/uploads", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("breakbar-spa", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        string csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Hashed_assets_are_cached_forever()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/assets/app-abc123.js", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_api_path_is_404_not_the_spa()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
