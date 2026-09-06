namespace TaskForge.Core.RateLimiting;

/// <summary>
/// Queue-level concurrency settings.
/// </summary>
public class QueueConcurrencyConfig
{
    public string QueueName { get; set; } = "default";
    public string Namespace { get; set; } = "default";
    public int MaxConcurrency { get; set; } = 5;
    public int MaxQueueSize { get; set; } = 10000;
}

/// <summary>
/// Rate limiting configuration.
/// </summary>
public class RateLimitingConfiguration
{
    public bool Enabled { get; set; } = true;
    public int DefaultRequestsPerSecond { get; set; } = 100;
    public int DefaultBurstSize { get; set; } = 200;
    public List<QueueConcurrencyConfig> QueueConcurrency { get; set; } = new();
}
