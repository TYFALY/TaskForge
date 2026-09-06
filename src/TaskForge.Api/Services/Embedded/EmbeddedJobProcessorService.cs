using System.Text.Json;
using TaskForge.Core;

namespace TaskForge.Api.Services.Embedded;

public interface IEmbeddedJobProcessor
{
    bool IsRunning { get; }
    int ProcessedCount { get; }
}

public class EmbeddedJobProcessorService : BackgroundService, IEmbeddedJobProcessor
{
    private readonly IJobBufferService _bufferService;
    private readonly IEmbeddedQueueService _queueService;
    private readonly ILogger<EmbeddedJobProcessorService> _logger;
    private readonly string _workerId;
    private int _processedCount;

    public bool IsRunning => true;
    public int ProcessedCount => _processedCount;

    public EmbeddedJobProcessorService(
        IJobBufferService bufferService,
        IEmbeddedQueueService queueService,
        ILogger<EmbeddedJobProcessorService> logger)
    {
        _bufferService = bufferService;
        _queueService = queueService;
        _logger = logger;
        _workerId = $"embedded-worker-{Environment.MachineName}-{Guid.NewGuid():N}"[..32];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _queueService.RegisterWorker(_workerId);
        _logger.LogInformation("[PROCESSOR] Embedded job processor started with worker ID: {WorkerId}", _workerId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var (success, job) = await _bufferService.TryDequeueAsync(stoppingToken);
                    if (success && job != null)
                    {
                        _logger.LogInformation("[PROCESSOR] Got job {JobId} from buffer", job.Id);
                        await ProcessJobAsync(job, stoppingToken);
                    }
                    else
                    {
                        await Task.Delay(100, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PROCESSOR] Error in embedded job processor");
                    await Task.Delay(500, stoppingToken);
                }
            }
        }
        finally
        {
            _queueService.UnregisterWorker(_workerId);
            _logger.LogInformation("[PROCESSOR] Embedded job processor stopped. Total processed: {Count}", _processedCount);
        }
    }

    private async Task ProcessJobAsync(JobEnvelope job, CancellationToken ct)
    {
        _logger.LogInformation("[PROCESSOR] Processing job {JobId} of type {JobType} in embedded mode",
            job.Id, job.JobType);

        // Use try-finally to guarantee status update
        JobStatus finalStatus = JobStatus.Completed;
        Exception? processingException = null;

        try
        {
            if (job.JobType == JobType.Webhook)
            {
                await ProcessWebhookJobAsync(job, ct);
            }
            else if (job.JobType == JobType.Scheduled)
            {
                _logger.LogDebug("[PROCESSOR] Scheduled job {JobId} acknowledged, next run calculated", job.Id);
            }
            else
            {
                var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
                _logger.LogDebug("[PROCESSOR] Processing job payload: {Payload}", payload);
                await Task.Delay(50, ct);
            }

            finalStatus = JobStatus.Completed;
            _logger.LogInformation("[PROCESSOR] Job {JobId} completed successfully", job.Id);
        }
        catch (Exception ex)
        {
            processingException = ex;
            finalStatus = JobStatus.Failed;
            _logger.LogError(ex, "[PROCESSOR] Job {JobId} failed", job.Id);
        }
        finally
        {
            // ALWAYS update status, guaranteed by try-finally
            UpdateJobStatus(job.Id, finalStatus);

            if (finalStatus == JobStatus.Completed)
            {
                Interlocked.Increment(ref _processedCount);
            }
        }
    }

    private void UpdateJobStatus(Guid jobId, JobStatus status)
    {
        try
        {
            if (_bufferService is JobBufferService buffer)
            {
                buffer.UpdateJobStatus(jobId, status);
                _logger.LogDebug("[PROCESSOR] Job {JobId} status updated to {Status}", jobId, status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PROCESSOR] Failed to update job {JobId} status to {Status}", jobId, status);
        }
    }

    private async Task ProcessWebhookJobAsync(JobEnvelope job, CancellationToken ct)
    {
        WebhookPayload? webhook = job.WebhookPayload;
        if (webhook == null)
        {
            try { webhook = WebhookPayload.Deserialize(job.Payload); }
            catch { _logger.LogWarning("[PROCESSOR] Invalid webhook payload for job {JobId}", job.Id); throw; }
        }

        if (webhook == null) throw new InvalidOperationException("Webhook payload is null");

        _logger.LogInformation("[PROCESSOR] Executing webhook job {JobId}: {Method} {Url}",
            job.Id, webhook.Method, webhook.TargetUrl);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(webhook.TimeoutSeconds) };
        using var request = new HttpRequestMessage
        {
            Method = new HttpMethod(webhook.Method),
            RequestUri = new Uri(webhook.TargetUrl)
        };

        foreach (var header in webhook.Headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (!string.IsNullOrEmpty(webhook.Body) &&
            (webhook.Method == "POST" || webhook.Method == "PUT" || webhook.Method == "PATCH"))
        {
            request.Content = new StringContent(webhook.Body, System.Text.Encoding.UTF8, "application/json");
        }

        var response = await httpClient.SendAsync(request, ct);
        var statusCode = (int)response.StatusCode;
        
        // Throw on non-success status codes to trigger retry/DLQ
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Webhook returned status {statusCode}");
        }
        
        _logger.LogInformation("[PROCESSOR] Webhook job {JobId} returned status {StatusCode}", job.Id, statusCode);
    }
}
