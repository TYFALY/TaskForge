using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TaskForge.Api.Metrics;
using TaskForge.Api.Services;
using TaskForge.Api.Services.Embedded;
using TaskForge.Core;
using TaskForge.Core.Auth;

namespace TaskForge.Api.Endpoints;

public static class JobsEndpoint
{
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapJobsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/jobs")
            .WithTags("Jobs");

        group.MapPost("/enqueue", EnqueueJob)
            .WithName("EnqueueJob")
            .WithSummary("Enqueue a new job")
            .Produces<EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests);

        group.MapPost("/webhook", EnqueueWebhook)
            .WithName("EnqueueWebhook")
            .WithSummary("Enqueue a webhook job")
            .Produces<EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests);

        group.MapPost("/scheduled", EnqueueScheduledJob)
            .WithName("EnqueueScheduledJob")
            .WithSummary("Enqueue a scheduled job with cron expression")
            .Produces<ScheduledJobResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests);

        group.MapGet("/{jobId:guid}", GetJobStatus)
            .WithName("GetJobStatus")
            .WithSummary("Get the status of a job")
            .Produces<EnqueueJobResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{jobId:guid}/retry", RetryJob)
            .WithName("RetryJob")
            .WithSummary("Retry/replay a failed or dead-lettered job")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{jobId:guid}/cancel", CancelJob)
            .WithName("CancelJob")
            .WithSummary("Cancel an active or queued job")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/dlq/replay-all", ReplayAllDlq)
            .WithName("ReplayAllDlq")
            .WithSummary("Replay all jobs in Dead-Letter status")
            .Produces(StatusCodes.Status200OK);
    }

    private static async Task<IResult> EnqueueJob(
        [FromBody] EnqueueJobRequest request,
        [FromServices] IJobBufferService bufferService,
        [FromServices] ILogger<Program> logger,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.QueueName))
            request.QueueName = "default";

        if (string.IsNullOrWhiteSpace(request.Payload))
            return Results.BadRequest(new { error = "Payload is required" });

        try
        {
            var @namespace = context.Items.TryGetValue("Namespace", out var ns) ? ns?.ToString() ?? request.Namespace : request.Namespace;
            var tenantId = context.Items.TryGetValue("TenantId", out var tid) ? tid?.ToString() ?? request.TenantId : request.TenantId;

            var job = JobEnvelope.Create(
                queueName: request.QueueName,
                payload: request.Payload,
                maxRetries: request.MaxRetries,
                @namespace: @namespace,
                tenantId: tenantId
            );

            var buffered = await bufferService.EnqueueAsync(job);
            if (!buffered)
            {
                logger.LogError("Failed to buffer job {JobId}", job.Id);
                return Results.Problem("Failed to buffer job for processing");
            }

            TaskForgeMetrics.RecordJobEnqueued(job.QueueName, job.Namespace, job.TenantId, "default");

            logger.LogInformation("Job {JobId} enqueued to queue {QueueName} in namespace {Namespace}",
                job.Id, job.QueueName, job.Namespace);

            var response = new EnqueueJobResponse
            {
                JobId = job.Id,
                QueueName = job.QueueName,
                Status = job.Status.ToString(),
                EnqueuedAt = job.EnqueuedAt,
                Namespace = job.Namespace,
                TenantId = job.TenantId,
                JobType = job.JobType
            };

            return Results.Accepted($"/api/v1/jobs/{job.Id}", response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enqueueing job");
            return Results.Problem("An error occurred while enqueueing the job");
        }
    }

    private static async Task<IResult> EnqueueWebhook(
        [FromBody] EnqueueWebhookRequest request,
        [FromServices] IJobBufferService bufferService,
        [FromServices] ILogger<Program> logger,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.TargetUrl))
            return Results.BadRequest(new { error = "TargetUrl is required" });

        try
        {
            var @namespace = context.Items.TryGetValue("Namespace", out var ns) ? ns?.ToString() ?? request.Namespace : request.Namespace;
            var tenantId = context.Items.TryGetValue("TenantId", out var tid) ? tid?.ToString() ?? request.TenantId : request.TenantId;

            var webhookPayload = new WebhookPayload
            {
                TargetUrl = request.TargetUrl,
                Method = request.Method,
                Headers = request.Headers ?? new Dictionary<string, string>(),
                Body = request.Body,
                TimeoutSeconds = request.TimeoutSeconds,
                VerifySsl = request.VerifySsl
            };

            webhookPayload.NormalizeBody();

            var job = JobEnvelope.CreateWebhook(
                queueName: request.QueueName,
                webhook: webhookPayload,
                maxRetries: request.MaxRetries,
                @namespace: @namespace,
                tenantId: tenantId
            );

            var buffered = await bufferService.EnqueueAsync(job);
            if (!buffered)
            {
                logger.LogError("Failed to buffer webhook job {JobId}", job.Id);
                return Results.Problem("Failed to buffer webhook job for processing");
            }

            TaskForgeMetrics.RecordJobEnqueued(job.QueueName, job.Namespace, job.TenantId, "webhook");

            logger.LogInformation("Webhook job {JobId} enqueued: {Method} {Url}",
                job.Id, webhookPayload.Method, webhookPayload.TargetUrl);

            var response = new EnqueueJobResponse
            {
                JobId = job.Id,
                QueueName = job.QueueName,
                Status = job.Status.ToString(),
                EnqueuedAt = job.EnqueuedAt,
                Namespace = job.Namespace,
                TenantId = job.TenantId,
                JobType = job.JobType
            };

            return Results.Accepted($"/api/v1/jobs/{job.Id}", response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enqueueing webhook job");
            return Results.Problem("An error occurred while enqueueing the webhook job");
        }
    }

    private static async Task<IResult> EnqueueScheduledJob(
        [FromBody] EnqueueScheduledJobRequest request,
        [FromServices] IJobBufferService bufferService,
        [FromServices] ILogger<Program> logger,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.CronExpression))
            return Results.BadRequest(new { error = "CronExpression is required" });

        if (string.IsNullOrWhiteSpace(request.Payload))
            return Results.BadRequest(new { error = "Payload is required" });

        try
        {
            var @namespace = context.Items.TryGetValue("Namespace", out var ns) ? ns?.ToString() ?? request.Namespace : request.Namespace;
            var tenantId = context.Items.TryGetValue("TenantId", out var tid) ? tid?.ToString() ?? request.TenantId : request.TenantId;

            var job = JobEnvelope.CreateScheduled(
                queueName: request.QueueName,
                payload: request.Payload,
                cronExpression: request.CronExpression,
                maxRetries: request.MaxRetries,
                @namespace: @namespace,
                tenantId: tenantId
            );

            var buffered = await bufferService.EnqueueAsync(job);
            if (!buffered)
            {
                logger.LogError("Failed to buffer scheduled job {JobId}", job.Id);
                return Results.Problem("Failed to buffer scheduled job for processing");
            }

            TaskForgeMetrics.RecordJobEnqueued(job.QueueName, job.Namespace, job.TenantId, "scheduled");

            logger.LogInformation("Scheduled job {JobId} enqueued with cron: {Cron}",
                job.Id, request.CronExpression);

            var response = new ScheduledJobResponse
            {
                JobId = job.Id,
                QueueName = job.QueueName,
                CronExpression = request.CronExpression,
                NextScheduledTime = job.GetNextScheduledTime(),
                EnqueuedAt = job.EnqueuedAt,
                Namespace = job.Namespace
            };

            return Results.Accepted($"/api/v1/jobs/{job.Id}", response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error enqueueing scheduled job");
            return Results.Problem("An error occurred while enqueueing the scheduled job");
        }
    }

    private static async Task<IResult> GetJobStatus(Guid jobId, [FromServices] IJobBufferService bufferService, [FromServices] ILogger<Program> logger)
    {
        try
        {
            // Try to get the job status from the buffer first
            var job = await bufferService.GetJobAsync(jobId);
            if (job != null)
            {
                return Results.Ok(new EnqueueJobResponse
                {
                    JobId = job.Id,
                    QueueName = job.QueueName,
                    Status = job.Status.ToString(),
                    EnqueuedAt = job.EnqueuedAt,
                    Namespace = job.Namespace,
                    TenantId = job.TenantId,
                    JobType = job.JobType
                });
            }

            // If not in buffer, return 404
            return Results.NotFound(new { error = "Job not found", jobId = jobId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting job status for {JobId}", jobId);
            return Results.Problem("An error occurred while getting job status");
        }
    }

    private static async Task<IResult> RetryJob(
        Guid jobId,
        [FromServices] IJobBufferService bufferService,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            var success = await bufferService.RetryJobAsync(jobId);
            if (!success)
            {
                return Results.NotFound(new { error = "Job not found", jobId });
            }

            return Results.Ok(new { success = true, message = $"Job {jobId} replayed successfully", jobId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrying job {JobId}", jobId);
            return Results.Problem("An error occurred while retrying job");
        }
    }

    private static async Task<IResult> CancelJob(
        Guid jobId,
        [FromServices] IJobBufferService bufferService,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            var job = await bufferService.GetJobAsync(jobId);
            if (job == null)
            {
                return Results.NotFound(new { error = "Job not found", jobId });
            }

            if (job.Status == JobStatus.Completed)
            {
                return Results.BadRequest(new { error = "Cannot cancel an already completed job", jobId });
            }

            var cancelled = await bufferService.CancelJobAsync(jobId);
            return Results.Ok(new { success = cancelled, message = $"Job {jobId} cancelled successfully", jobId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cancelling job {JobId}", jobId);
            return Results.Problem("An error occurred while cancelling job");
        }
    }

    private static async Task<IResult> ReplayAllDlq(
        [FromServices] IJobBufferService bufferService,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            var count = await bufferService.ReplayAllDlqAsync();
            return Results.Ok(new { success = true, replayedCount = count, message = $"Replayed {count} dead-lettered job(s)" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error replaying dead-lettered jobs");
            return Results.Problem("An error occurred while replaying dead-lettered jobs");
        }
    }
}



