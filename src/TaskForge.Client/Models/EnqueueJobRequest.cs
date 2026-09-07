using System.Text.Json.Serialization;

namespace TaskForge.Client.Models;

public class EnqueueJobRequest
{
    [JsonPropertyName("queueName")]
    public string QueueName { get; set; } = "default";

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "{}";

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }
}
