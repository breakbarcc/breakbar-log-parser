using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Breakbar.Api.Data;
using Breakbar.Api.Identity;
using Breakbar.Api.Jobs;
using Breakbar.Api.Options;
using Breakbar.Processing.Queue;
using Microsoft.AspNetCore.Http.Features;

namespace Breakbar.Api.Endpoints;

public static class JobEndpoints
{
    public const string UploadRateLimitPolicy = "uploads";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    public static void MapJobEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Raw body upload, one file per request: POST /api/uploads?name=foo.zevtc&batch=<id>
        api.MapPost("/uploads", async (HttpContext context, string? name, string? batch, UserIdentity identity,
                UploadService uploads, UploadOptions options, CancellationToken ct) =>
            {
                if (identity.Resolve(context) is not { } userId)
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "Invalid API key.");
                }
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
                {
                    sizeLimit.MaxRequestBodySize = options.MaxUploadBytes;
                }
                var outcome = await uploads.HandleAsync(userId, name, batch, context.Request.Body, ct);
                return outcome.Job is not null
                    ? Results.Json(outcome.Job, statusCode: outcome.StatusCode)
                    : Results.Problem(statusCode: outcome.StatusCode, detail: outcome.Error);
            })
            .RequireRateLimiting(UploadRateLimitPolicy);

        api.MapGet("/jobs", (HttpContext context, string? batch, UserIdentity identity, JobStore store, FairParseQueue queue) =>
        {
            if (identity.Resolve(context) is not { } userId)
            {
                return Results.Unauthorized();
            }
            var positions = queue.GetPositions(userId);
            var jobs = store.ListForUser(userId, batch, limit: 500)
                .Select(j => JobDto.From(j, positions.TryGetValue(Guid.ParseExact(j.Id, "N"), out int p) ? p : null));
            return Results.Ok(jobs);
        });

        api.MapGet("/jobs/{id}", (HttpContext context, string id, UserIdentity identity, JobStore store, FairParseQueue queue) =>
        {
            if (identity.Resolve(context) is not { } userId)
            {
                return Results.Unauthorized();
            }
            if (!Guid.TryParseExact(id, "N", out var jobId) || store.GetView(id) is not { } job || job.UserId != userId)
            {
                return Results.NotFound();
            }
            return Results.Ok(JobDto.From(job, queue.GetPosition(jobId)));
        });

        // Cancels a job that has not started yet.
        api.MapDelete("/jobs/{id}", (HttpContext context, string id, UserIdentity identity, JobStore store,
            FairParseQueue queue, JobEventHub hub) =>
        {
            if (identity.Resolve(context) is not { } userId)
            {
                return Results.Unauthorized();
            }
            if (!Guid.TryParseExact(id, "N", out var jobId) || store.GetJob(id) is not { } job || job.UserId != userId)
            {
                return Results.NotFound();
            }
            if (queue.TryRemove(jobId) is null)
            {
                return Results.Conflict(new { error = "Job is not waiting anymore." });
            }
            store.MarkCancelled(id);
            File.Delete(job.UploadPath);
            hub.PublishJob(userId, JobDto.From(store.GetView(id)!, null));
            hub.PublishPositions();
            return Results.NoContent();
        });

        // Server-Sent Events: "job" (status change of one job), "positions" (queue moved), "ping" (keep-alive).
        api.MapGet("/events", (HttpContext context, UserIdentity identity, JobEventHub hub, CancellationToken ct) =>
            identity.Resolve(context) is { } userId
                ? TypedResults.ServerSentEvents(Stream(hub.Subscribe(userId), ct))
                : Results.Unauthorized());

        api.MapGet("/health", (FairParseQueue queue, Processing.EliteInsights.EliteInsightsAdapter ei) =>
        {
            var stats = queue.GetStats();
            return Results.Ok(new
            {
                status = "ok",
                eliteInsights = ei.EliteInsightsVersion.ToString(),
                queue = new { stats.Queued, stats.Running, stats.ActiveUsers, reservedMemoryMb = stats.ReservedMemoryBytes / (1024 * 1024) },
            });
        });
    }

    private static async IAsyncEnumerable<SseItem<object>> Stream(JobEventHub.Subscription subscription, [EnumeratorCancellation] CancellationToken ct)
    {
        using (subscription)
        {
            yield return new SseItem<object>(new { }, "ready");
            while (!ct.IsCancellationRequested)
            {
                var waitForEvent = subscription.Reader.WaitToReadAsync(ct).AsTask();
                var finished = await Task.WhenAny(waitForEvent, Task.Delay(HeartbeatInterval, ct));
                if (finished != waitForEvent)
                {
                    yield return new SseItem<object>(new { }, "ping");
                    continue;
                }
                if (!await waitForEvent)
                {
                    yield break;
                }
                while (subscription.Reader.TryRead(out var jobEvent))
                {
                    yield return new SseItem<object>(jobEvent.Data, jobEvent.Type);
                }
            }
        }
    }
}
