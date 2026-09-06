using System.ComponentModel.DataAnnotations;

namespace TaskForge.Core;

public class EnqueueJobRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string QueueName { get; set; } = "default";

    [Required]
    public string Payload { get; set; } = "{}";

    [Range(1, 10)]
    public int MaxRetries { get; set; } = 3;

    public string Namespace { get; set; } = "default";
    public string? TenantId { get; set; }
}

public class EnqueueWebhookRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string QueueName { get; set; } = "webhooks";

    [Required]
    [Url]
    public string TargetUrl { get; set; } = string.Empty;

    public string Method { get; set; } = "POST";

    public Dictionary<string, string> Headers { get; set; } = new();

    public string? Body { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public bool VerifySsl { get; set; } = true;

    [Range(1, 10)]
    public int MaxRetries { get; set; } = 3;

    public string Namespace { get; set; } = "default";
    public string? TenantId { get; set; }
}

public class EnqueueScheduledJobRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string QueueName { get; set; } = "default";

    [Required]
    public string Payload { get; set; } = "{}";

    [Required]
    [RegularExpression(@"^(\*|([0-9]|1[0-9]|2[0-9]|3[0-9]|4[0-9]|5[0-9])|\*\/([0-9]|1[0-9]|2[0-9]|3[0-9]|4[0-9]|5[0-9])) (\*|([0-9]|1[0-9]|2[0-3])|\*\/([0-9]|1[0-9]|2[0-3])) (\*|([1-9]|1[0-9]|2[0-9]|3[0-1])|\*\/([1-9]|1[0-9]|2[0-9]|3[0-1])) (\*|([1-9]|1[0-2])|\*\/([1-9]|1[0-2])) (\*|([0-6])|\*\/([0-6]))$",
        ErrorMessage = "Invalid cron expression")]
    public string CronExpression { get; set; } = string.Empty;

    [Range(1, 10)]
    public int MaxRetries { get; set; } = 3;

    public string Namespace { get; set; } = "default";
    public string? TenantId { get; set; }
}

public class EnqueueJobResponse
{
    public Guid JobId { get; set; }
    public string QueueName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime EnqueuedAt { get; set; }
    public string Namespace { get; set; } = "default";
    public string? TenantId { get; set; }
    public JobType JobType { get; set; }
    public DateTime? NextScheduledTime { get; set; }
}

public class ScheduledJobResponse
{
    public Guid JobId { get; set; }
    public string QueueName { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public DateTime? NextScheduledTime { get; set; }
    public DateTime EnqueuedAt { get; set; }
    public string Namespace { get; set; } = "default";
}

public class QueueMetrics
{
    public long QueueSize { get; set; }
    public int ActiveWorkers { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class HealthCheckResponse
{
    public string Status { get; set; } = "healthy";
    public string Version { get; set; } = "1.0.0";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Components { get; set; } = new();
}
