using Breakbar.Processing.EliteInsights;

// Skeleton only: upload, fair queue, SSE and storage follow in later steps (docs/PLAN.md, section 7).
var builder = WebApplication.CreateBuilder(args);

var eiOptions = builder.Configuration.GetSection("EliteInsights").Get<EliteInsightsOptions>()
    ?? throw new InvalidOperationException("Missing configuration section 'EliteInsights'.");
builder.Services.AddSingleton(eiOptions);
builder.Services.AddSingleton<EliteInsightsAdapter>();

var app = builder.Build();

app.MapGet("/api/health", (EliteInsightsAdapter ei) => Results.Ok(new
{
    status = "ok",
    eliteInsights = ei.EliteInsightsVersion.ToString(),
}));

app.Run();
