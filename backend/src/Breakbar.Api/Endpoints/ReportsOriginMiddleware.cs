using Breakbar.Api.Options;

namespace Breakbar.Api.Endpoints;

/// <summary>
/// Keeps report pages on their own origin (docs/PLAN.md 4b).
///
/// Report HTML runs Elite Insights' JavaScript plus scripts from public CDNs, and the log data inside it is
/// attacker-controlled (EVTC files are not signed). If a report could run script on the API origin, it could call
/// /api/jobs with the visitor's cookie and collect their report links. On a separate origin the browser sends no
/// API cookie (host-only) and the API sends no CORS headers, so a report page cannot read API responses.
///
/// With <see cref="ReportsOptions.Host"/> configured:
/// - reports host: only /r/* and /ei-assets/* are served, everything else is 404,
/// - any other host: /r/* redirects to the reports host, /ei-assets/* is 404.
/// </summary>
public static class ReportsOriginMiddleware
{
    public static void UseReportsOriginSeparation(this WebApplication app, ReportsOptions reports)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            bool isReport = path.StartsWithSegments("/r");
            bool isAsset = path.StartsWithSegments("/ei-assets");

            if (reports.Host is { } reportsHost)
            {
                bool onReportsHost = string.Equals(context.Request.Host.Host, reportsHost, StringComparison.OrdinalIgnoreCase);
                if (onReportsHost != (isReport || isAsset))
                {
                    if (!onReportsHost && isReport)
                    {
                        context.Response.Redirect(reports.BaseUrl!.TrimEnd('/') + path + context.Request.QueryString, permanent: true);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                    }
                    return;
                }
            }

            if (isReport || isAsset)
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";            // report ids must not leak to the CDNs EI loads
                headers["X-Robots-Tag"] = "noindex, nofollow";         // reports are "public by link", not for search engines
                headers.ContentSecurityPolicy = "frame-ancestors 'none'";
            }
            else
            {
                // Frontend and API: everything is self-hosted (fonts included), so a strict policy is possible.
                // 'unsafe-inline' for styles only, because React renders style attributes.
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "same-origin";
                headers.ContentSecurityPolicy =
                    "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
                    "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
            }
            await next(context);
        });
    }
}
