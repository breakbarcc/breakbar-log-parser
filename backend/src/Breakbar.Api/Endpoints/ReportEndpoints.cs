using Breakbar.Api.Options;
using Microsoft.Net.Http.Headers;

namespace Breakbar.Api.Endpoints;

/// <summary>
/// Report delivery. In production Caddy serves these paths straight from disk (deploy/logs.caddy);
/// these endpoints keep local development and tests working without Caddy.
/// </summary>
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapGet("/r/{id}", (string id, StorageOptions storage) =>
        {
            string? path = ReportFile(storage, id, "report.html");
            return path is null ? Results.NotFound() : Results.File(path, "text/html; charset=utf-8");
        });

        // EI JSON export; stored gzip-compressed and sent as-is with Content-Encoding: gzip.
        app.MapGet("/r/{id}/json", (HttpContext context, string id, StorageOptions storage) =>
        {
            string? path = ReportFile(storage, id, "report.json.gz");
            if (path is null)
            {
                return Results.NotFound();
            }
            context.Response.Headers[HeaderNames.ContentEncoding] = "gzip";
            return Results.File(path, "application/json");
        });
    }

    private static string? ReportFile(StorageOptions storage, string id, string fileName)
    {
        if (!ShortId.IsValid(id))
        {
            return null;
        }
        string path = Path.GetFullPath(Path.Combine(storage.ReportDirectory(id), fileName));
        return File.Exists(path) ? path : null;
    }
}
