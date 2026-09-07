using System.Text.Json;
using TaskForge.Core;
using TaskForge.Core.Security;

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
    private readonly int _maxRetries;
    private int _processedCount;

    public bool IsRunning => true;
    public int ProcessedCount => _processedCount;

    public EmbeddedJobProcessorService(
        IJobBufferService bufferService,
        IEmbeddedQueueService queueService,
        ILogger<EmbeddedJobProcessorService> logger,
        int maxRetries = 3)
    {
        _bufferService = bufferService;
        _queueService = queueService;
        _logger = logger;
        _workerId = $"embedded-worker-{Environment.MachineName}-{Guid.NewGuid():N}"[..32];
        _maxRetries = maxRetries;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _queueService.RegisterWorker(_workerId);
        _logger.LogInformation("[PROCESSOR] Embedded job processor started with worker ID: {WorkerId}, MaxRetries: {MaxRetries}", _workerId, _maxRetries);

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

        var attempt = job.CurrentRetry;

        while (true)
        {
            try
            {
                await ExecuteJobAsync(job, ct);
                _logger.LogInformation("[PROCESSOR] Job {JobId} completed successfully", job.Id);
                UpdateJobStatus(job.Id, JobStatus.Completed);
                Interlocked.Increment(ref _processedCount);
                return;
            }
            catch (Exception ex)
            {
                attempt++;

                if (attempt >= _maxRetries)
                {
                    _logger.LogWarning(ex, "[PROCESSOR] Job {JobId} exhausted {MaxRetries} retries. Marking as DeadLettered.",
                        job.Id, _maxRetries);
                    UpdateJobStatus(job.Id, JobStatus.DeadLettered);
                    return;
                }

                var delaySeconds = (int)Math.Pow(2, attempt);
                _logger.LogWarning(ex, "[PROCESSOR] Job {JobId} failed (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}s...",
                    job.Id, attempt, _maxRetries, delaySeconds);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("[PROCESSOR] Job {JobId} retry delay cancelled, re-enqueuing", job.Id);
                    await _queueService.PushJobAsync(job.QueueName, job with { CurrentRetry = attempt });
                    return;
                }
            }
        }
    }

    private async Task ExecuteJobAsync(JobEnvelope job, CancellationToken ct)
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
    }

    private void UpdateJobStatus(Guid jobId, JobStatus status)
    {
        try
        {
            _bufferService.UpdateJobStatus(jobId, status);
            _logger.LogDebug("[PROCESSOR] Job {JobId} status updated to {Status}", jobId, status);
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

        // SSRF Protection: validate URL before making any HTTP request
        SsrfProtectionFilter.EnsureSafe(webhook.TargetUrl);

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
        
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Webhook returned status {statusCode}");
        }
        
        _logger.LogInformation("[PROCESSOR] Webhook job {JobId} returned status {StatusCode}", job.Id, statusCode);
    }
}
