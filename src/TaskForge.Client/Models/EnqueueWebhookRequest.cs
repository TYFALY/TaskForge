using System.Text.Json.Serialization;

namespace TaskForge.Client.Models;

public class EnqueueWebhookRequest
{
    [JsonPropertyName("queueName")]
    public string QueueName { get; set; } = "webhooks";

    [JsonPropertyName("targetUrl")]
    public string TargetUrl { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = "POST";

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; set; } = 3;

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }
}
