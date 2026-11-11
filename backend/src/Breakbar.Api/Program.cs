using System.Threading.RateLimiting;
using Breakbar.Api.Persistence;
using Breakbar.Api.Endpoints;
using Breakbar.Api.Identity;
using Breakbar.Api.Jobs;
using Breakbar.Api.Options;
using Breakbar.Processing.EliteInsights;
using Breakbar.Processing.Queue;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Options ---------------------------------------------------------------------------------
var storage = config.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
storage.DataDirectory = Path.GetFullPath(storage.DataDirectory, builder.Environment.ContentRootPath);
Directory.CreateDirectory(storage.AssetsDirectory);

var eiOptions = new EliteInsightsOptions
{
    AssetsDirectory = storage.AssetsDirectory,
    AssetsBaseUrl = config["EliteInsights:AssetsBaseUrl"] ?? "/ei-assets",
    ParseCombatReplay = config.GetValue("EliteInsights:ParseCombatReplay", true),
};

builder.Services.AddSingleton(storage);
builder.Services.AddSingleton(eiOptions);
builder.Services.AddSingleton(config.GetSection("Queue").Get<ParseQueueOptions>() ?? new ParseQueueOptions());
builder.Services.AddSingleton(config.GetSection("Uploads").Get<UploadOptions>() ?? new UploadOptions());
builder.Services.AddSingleton(config.GetSection("ServiceClients").Get<ServiceClientOptions>() ?? new ServiceClientOptions());
builder.Services.AddSingleton(TimeProvider.System);

// ---- Services --------------------------------------------------------------------------------
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<EliteInsightsAdapter>();
builder.Services.AddSingleton<IEliteInsightsParser>(sp => sp.GetRequiredService<EliteInsightsAdapter>());
builder.Services.AddSingleton<FairParseQueue>();
builder.Services.AddSingleton<ParseCostEstimator>();
builder.Services.AddSingleton<JobEventHub>();
builder.Services.AddSingleton<JobLifecycle>();
builder.Services.AddSingleton<IParseJobObserver>(sp => sp.GetRequiredService<JobLifecycle>());
builder.Services.AddSingleton<UploadService>();
builder.Services.AddSingleton<UserIdentity>();
builder.Services.AddHostedService<ParseWorkerPool>();

// Behind Caddy all requests come from 127.0.0.1; take the client IP from X-Forwarded-For (loopback proxies are trusted by default).
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(JobEndpoints.UploadRateLimitPolicy, context =>
    {
        var uploads = context.RequestServices.GetRequiredService<UploadOptions>();
        if (context.RequestServices.GetRequiredService<UserIdentity>().HasValidApiKey(context))
        {
            return RateLimitPartition.GetNoLimiter("service");
        }
        return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = uploads.UploadsPerMinutePerIp, Window = TimeSpan.FromMinutes(1) });
    });
});

var app = builder.Build();

// ---- Startup: schema, then re-queue jobs interrupted by the last shutdown (before workers start) ----
app.Services.GetRequiredService<Database>().Initialize();
int recovered = app.Services.GetRequiredService<JobLifecycle>().RecoverPendingJobs();
if (recovered > 0)
{
    app.Logger.LogInformation("Re-queued {Count} pending jobs from the last run", recovered);
}

app.UseForwardedHeaders();
app.UseRateLimiter();

// EI's JS/CSS for the reports. In production Caddy serves /ei-assets directly.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storage.AssetsDirectory),
    RequestPath = "/ei-assets",
});

app.MapJobEndpoints();
app.MapReportEndpoints();

app.Run();

/// <summary>Entry point; public for WebApplicationFactory in the API tests.</summary>
public partial class Program;
