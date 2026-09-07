using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaskForge.Core;
using TaskForge.Core.Security;

namespace TaskForge.Worker.Handlers;

public interface IJobHandler
{
    JobType SupportedType { get; }
    Task<JobExecutionResult> ExecuteAsync(JobEnvelope job, CancellationToken ct);
}

public class JobExecutionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Result { get; set; }
    public int StatusCode { get; set; }

    public static JobExecutionResult Succeeded(string? result = null, int statusCode = 200) =>
        new() { Success = true, Result = result, StatusCode = statusCode };

    public static JobExecutionResult Failed(string error, int statusCode = 500) =>
        new() { Success = false, ErrorMessage = error, StatusCode = statusCode };
}

public class WebhookJobHandler : IJobHandler
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookJobHandler> _logger;

    public JobType SupportedType => JobType.Webhook;

    public WebhookJobHandler(HttpClient httpClient, ILogger<WebhookJobHandler> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<JobExecutionResult> ExecuteAsync(JobEnvelope job, CancellationToken ct)
    {
        WebhookPayload? webhook = job.WebhookPayload;
        if (webhook == null)
        {
            // Use case-insensitive deserialization for backward compatibility
            webhook = WebhookPayload.Deserialize(job.Payload);
            if (webhook == null)
                return JobExecutionResult.Failed("Invalid webhook payload", 400);
        }

        _logger.LogInformation("Executing webhook job {JobId}: {Method} {Url}",
            job.Id, webhook.Method, webhook.TargetUrl);

        // SSRF protection: reject loopback / metadata / private addresses
        try
        {
            SsrfProtectionFilter.EnsureSafe(webhook.TargetUrl);
        }
        catch (SsrfBlockedException ex)
        {
            _logger.LogWarning("Webhook job {JobId} blocked by SSRF filter: {Reason}", job.Id, ex.Message);
            return JobExecutionResult.Failed($"Blocked by SSRF filter: {ex.Message}", 400);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Webhook job {JobId} rejected invalid URL: {Reason}", job.Id, ex.Message);
            return JobExecutionResult.Failed($"Invalid webhook URL: {ex.Message}", 400);
        }

        try
        {
            var request = new HttpRequestMessage
            {
                Method = new HttpMethod(webhook.Method),
                RequestUri = new Uri(webhook.TargetUrl)
            };

            foreach (var header in webhook.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (!string.IsNullOrEmpty(webhook.Body) &&
                (webhook.Method == "POST" || webhook.Method == "PUT" || webhook.Method == "PATCH"))
            {
                request.Content = new StringContent(webhook.Body, Encoding.UTF8, "application/json");
            }

            _httpClient.Timeout = TimeSpan.FromSeconds(webhook.TimeoutSeconds);

            var stopwatch = Stopwatch.StartNew();
            var response = await _httpClient.SendAsync(request, ct);
            stopwatch.Stop();

            var statusCode = (int)response.StatusCode;
            var content = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("Webhook job {JobId} completed with status {StatusCode} in {Duration}ms",
                job.Id, statusCode, stopwatch.ElapsedMilliseconds);

            if (response.IsSuccessStatusCode)
            {
                return JobExecutionResult.Succeeded(content, statusCode);
            }

            return JobExecutionResult.Failed($"Webhook returned {statusCode}: {content}", statusCode);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Webhook job {JobId} was cancelled", job.Id);
            return JobExecutionResult.Failed("Job was cancelled", 499);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Webhook job {JobId} timed out after {Timeout}s",
                job.Id, webhook.TimeoutSeconds);
            return JobExecutionResult.Failed($"Request timed out after {webhook.TimeoutSeconds}s", 504);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Webhook job {JobId} failed with HTTP error", job.Id);
            return JobExecutionResult.Failed($"HTTP error: {ex.Message}", 502);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook job {JobId} failed with unexpected error", job.Id);
            return JobExecutionResult.Failed($"Error: {ex.Message}", 500);
        }
    }
}

public class DefaultJobHandler : IJobHandler
{
    private readonly ILogger<DefaultJobHandler> _logger;

    public JobType SupportedType => JobType.Default;

    public DefaultJobHandler(ILogger<DefaultJobHandler> logger)
    {
        _logger = logger;
    }

    public async Task<JobExecutionResult> ExecuteAsync(JobEnvelope job, CancellationToken ct)
    {
        _logger.LogInformation("Processing default job {JobId} on queue {Queue}",
            job.Id, job.QueueName);

        try
        {
            var payload = JsonSerializer.Deserialize<JsonElement>(job.Payload);
            _logger.LogDebug("Job payload: {Payload}", payload);

            await Task.Delay(100, ct);

            return JobExecutionResult.Succeeded($"Processed job {job.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process job {JobId}", ex.Message);
            return JobExecutionResult.Failed(ex.Message);
        }
    }
}
