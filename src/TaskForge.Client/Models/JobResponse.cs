using System.Text.Json.Serialization;

namespace TaskForge.Client.Models;

public class JobResponse
{
    [JsonPropertyName("jobId")]
    public Guid JobId { get; set; }

    [JsonPropertyName("queueName")]
    public string QueueName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("enqueuedAt")]
    public DateTime EnqueuedAt { get; set; }

    [JsonPropertyName("payload")]
    public string? Payload { get; set; }

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; }

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; }

    [JsonPropertyName("deadLetterReason")]
    public string? DeadLetterReason { get; set; }

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("jobType")]
    public string? JobType { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore]
    public bool IsTerminal =>
        Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("DeadLettered", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsSuccess => Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
}
