using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TaskForge.Api.Services;
using TaskForge.Core;

namespace TaskForge.Api.Endpoints;

public static class MetricsEndpoint
{
    public static void MapMetricsEndpoints(this WebApplication app)
    {
        app.MapGroup("/api/v1/metrics").MapGet("/stream", StreamMetrics);
    }

    private static async Task StreamMetrics(
        HttpContext context,
        [FromServices] IJobQueueService queueService,
        [FromServices] ILogger<Program> logger)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        var ct = context.RequestAborted;
        var queueName = context.Request.Query["queue"].ToString();
        if (string.IsNullOrWhiteSpace(queueName)) queueName = "default";

        logger.LogInformation("Metrics stream connected for queue {QueueName}, EmbeddedMode: {IsEmbedded}",
            queueName, queueService.IsEmbeddedMode);

        try
        {
            await SendEvent(context, queueService, queueName, ct);
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);
                await SendEvent(context, queueService, queueName, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { logger.LogError(ex, "Metrics stream error"); }
    }

    private static async Task SendEvent(HttpContext ctx, IJobQueueService svc, string q, CancellationToken ct)
    {
        try
        {
            var activeWorkers = svc.IsEmbeddedMode 
                ? 1  // In embedded mode, always report 1 active worker
                : await svc.GetActiveWorkerCountAsync();

            var metrics = new QueueMetrics
            {
                QueueSize = await svc.GetQueueSizeAsync(q),
                ActiveWorkers = activeWorkers,
                Timestamp = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(metrics, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var data = $"data: {json}\n\n";
            await ctx.Response.WriteAsync(data, ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
        catch { }
    }
}
