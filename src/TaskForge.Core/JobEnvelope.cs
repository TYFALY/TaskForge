using System.Text.Json.Serialization;
using Cronos;

namespace TaskForge.Core;

/// <summary>
/// Represents a job envelope containing all job data for queue processing.
/// </summary>
public record JobEnvelope
{
    public Guid Id { get; init; }
    public string QueueName { get; init; } = "default";
    public string Payload { get; init; } = "{}";
    public int MaxRetries { get; init; } = 3;
    public int CurrentRetry { get; init; }
    public JobStatus Status { get; init; } = JobStatus.Queued;
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
    public string Namespace { get; init; } = "default";
    public string? TenantId { get; init; }
    public string? CronSchedule { get; init; }
    public JobType JobType { get; init; } = JobType.Default;
    public WebhookPayload? WebhookPayload { get; init; }

    // [JsonIgnore]
    public string NamespacedQueueKey => $"taskforge:{Namespace}:queue:{QueueName}";

    public static JobEnvelope Create(string queueName, string payload, int maxRetries = 3,
        string @namespace = "default", string? tenantId = null,
        JobType jobType = JobType.Default, WebhookPayload? webhookPayload = null)
    {
        return new JobEnvelope
        {
            Id = Guid.NewGuid(),
            QueueName = queueName,
            Payload = payload,
            MaxRetries = maxRetries,
            CurrentRetry = 0,
            Status = JobStatus.Queued,
            EnqueuedAt = DateTime.UtcNow,
            Namespace = @namespace,
            TenantId = tenantId,
            JobType = jobType,
            WebhookPayload = webhookPayload
        };
    }

    public static JobEnvelope CreateScheduled(string queueName, string payload, string cronExpression,
        int maxRetries = 3, string @namespace = "default", string? tenantId = null)
    {
        CronExpression.Parse(cronExpression);
        return new JobEnvelope
        {
            Id = Guid.NewGuid(),
            QueueName = queueName,
            Payload = payload,
            MaxRetries = maxRetries,
            CurrentRetry = 0,
            Status = JobStatus.Scheduled,
            EnqueuedAt = DateTime.UtcNow,
            Namespace = @namespace,
            TenantId = tenantId,
            CronSchedule = cronExpression
        };
    }

    public static JobEnvelope CreateWebhook(string queueName, WebhookPayload webhook,
        int maxRetries = 3, string @namespace = "default", string? tenantId = null)
    {
        webhook.NormalizeBody();
        var payload = System.Text.Json.JsonSerializer.Serialize(webhook, WebhookPayload.CaseInsensitiveOptions);
        return new JobEnvelope
        {
            Id = Guid.NewGuid(),
            QueueName = queueName,
            Payload = payload,
            MaxRetries = maxRetries,
            CurrentRetry = 0,
            Status = JobStatus.Queued,
            EnqueuedAt = DateTime.UtcNow,
            Namespace = @namespace,
            TenantId = tenantId,
            JobType = JobType.Webhook,
            WebhookPayload = webhook
        };
    }

    public JobEnvelope IncrementRetry() => this with { CurrentRetry = CurrentRetry + 1, Status = JobStatus.Queued };
    public JobEnvelope MarkAsDeadLettered(string reason) => this with { Status = JobStatus.DeadLettered };
    public JobEnvelope MarkAsProcessing() => this with { Status = JobStatus.Processing };
    public JobEnvelope MarkAsCompleted() => this with { Status = JobStatus.Completed };

    // [JsonIgnore]
    public bool CanRetry => CurrentRetry < MaxRetries;

    // [JsonIgnore]
    public DateTime? GetNextScheduledTime()
    {
        if (string.IsNullOrEmpty(CronSchedule)) return null;
        try
        {
            var cron = CronExpression.Parse(CronSchedule);
            return cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
        }
        catch { return null; }
    }
}

public enum JobType { Default = 0, Scheduled = 1, Webhook = 2 }

public class WebhookPayload
{
    private static readonly System.Text.Json.JsonSerializerOptions _caseInsensitiveOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public static System.Text.Json.JsonSerializerOptions CaseInsensitiveOptions => _caseInsensitiveOptions;

    public string TargetUrl { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public System.Collections.Generic.Dictionary<string, string> Headers { get; set; } = new();
    public string? Body { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public bool VerifySsl { get; set; } = true;

    /// <summary>
    /// Deserializes a WebhookPayload from JSON string with case-insensitive property matching.
    /// </summary>
    public static WebhookPayload? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return System.Text.Json.JsonSerializer.Deserialize<WebhookPayload>(json, CaseInsensitiveOptions);
    }

    /// <summary>
    /// Serializes the WebhookPayload to a JSON string.
    /// </summary>
    public string Serialize()
    {
        return System.Text.Json.JsonSerializer.Serialize(this, CaseInsensitiveOptions);
    }

    /// <summary>
    /// Ensures the Body is a valid string, converting JSON objects if necessary.
    /// </summary>
    public void NormalizeBody()
    {
        if (Body == null)
            return;

        var trimmed = Body.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(Body);
                Body = jsonElement.GetRawText();
            }
            catch (System.Text.Json.JsonException)
            {
                // Already a valid JSON string, no change needed
            }
        }
    }
}