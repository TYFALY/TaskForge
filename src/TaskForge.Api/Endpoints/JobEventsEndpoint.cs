using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TaskForge.Api.Services;
using TaskForge.Core;

namespace TaskForge.Api.Endpoints;

public static class JobEventsEndpoint
{
    public static void MapJobEventsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/jobs")
            .WithTags("Job Events");

        // SSE stream for real-time job updates
        group.MapGet("/stream", HandleJobEventStream)
            .WithName("JobEventStream")
            .WithSummary("SSE stream for real-time job updates")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

        // GET all jobs (for initial load)
        group.MapGet("/", GetAllJobs)
            .WithName("GetAllJobs")
            .WithSummary("Get all jobs from buffer")
            .Produces<List<JobDto>>(StatusCodes.Status200OK);
    }

    private static async Task HandleJobEventStream(
        HttpContext context,
        [FromServices] IJobEventBroadcaster broadcaster,
        [FromServices] IJobBufferService bufferService,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers.AccessControlAllowOrigin = "*";

        await context.Response.Body.FlushAsync(cancellationToken);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromHours(24));
        var linkedCt = cts.Token;

        // Keep-alive task to send heartbeat every 30 seconds
        var keepAliveTask = Task.Run(async () =>
        {
            while (!linkedCt.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), linkedCt);
                    if (!linkedCt.IsCancellationRequested)
                    {
                        await context.Response.WriteAsync(": keepalive\r\n\r\n", linkedCt);
                        await context.Response.Body.FlushAsync(linkedCt);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* Client disconnected */ break; }
            }
        }, linkedCt);

        try
        {
            await foreach (var evt in broadcaster.SubscribeAsync(linkedCt))
            {
                if (linkedCt.IsCancellationRequested) break;

                try
                {
                    string data;
                    
                    if (evt.EventType == "job_enqueued" && evt.Data != null)
                    {
                        // For enqueued events, send the full job data
                        data = $"event: job_enqueued\r\ndata: {evt.Data}\r\n\r\n";
                    }
                    else
                    {
                        // For status updates, fetch the latest job data from buffer
                        var job = await bufferService.GetJobAsync(evt.JobId);
                        if (job != null)
                        {
                            var jobDto = new JobDto
                            {
                                Id = job.Id,
                                QueueName = job.QueueName,
                                Payload = job.Payload,
                                Status = job.Status.ToString(),
                                JobType = job.JobType.ToString(),
                                RetryCount = job.CurrentRetry,
                                MaxRetries = job.MaxRetries,
                                CreatedAt = job.EnqueuedAt,
                                UpdatedAt = job.EnqueuedAt
                            };
                            data = $"event: job_updated\r\ndata: {JsonSerializer.Serialize(jobDto)}\r\n\r\n";
                        }
                        else
                        {
                            // Job not found in buffer (may have been processed), send status only
                            data = $"event: job_updated\r\ndata: {{\"id\":\"{evt.JobId}\",\"status\":\"{evt.Status}\"}}\r\n\r\n";
                        }
                    }

                    await context.Response.WriteAsync(data, linkedCt);
                    await context.Response.Body.FlushAsync(linkedCt);
                }
                catch (Exception ex)
                {
                    // Log but do not break the stream
                    System.Console.WriteLine($"[SSE] Error sending event: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or timeout
        }
        finally
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private static IResult GetAllJobs([FromServices] IJobBufferService bufferService, int limit = 100)
    {
        var jobs = bufferService.GetAllJobs()
            .Take(limit)
            .Select(j => new JobDto
            {
                Id = j.Id,
                QueueName = j.QueueName,
                Payload = j.Payload,
                Status = j.Status.ToString(),
                JobType = j.JobType.ToString(),
                RetryCount = j.CurrentRetry,
                MaxRetries = j.MaxRetries,
                CreatedAt = j.EnqueuedAt,
                UpdatedAt = j.EnqueuedAt
            })
            .ToList();

        return Results.Ok(jobs);
    }
}

public class JobDto
{
    public Guid Id { get; set; }
    public string QueueName { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Status { get; set; } = "";
    public string JobType { get; set; } = "";
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
