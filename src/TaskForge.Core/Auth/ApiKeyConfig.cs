namespace TaskForge.Core.Auth;

/// <summary>
/// Represents an API key configuration.
/// </summary>
public class ApiKeyConfig
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = "default";
    public string? TenantId { get; set; }
    public List<string> AllowedQueues { get; set; } = new() { "*" };
    public int RateLimitPerSecond { get; set; } = 100;
    public int MaxConcurrencyPerQueue { get; set; } = 5;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Root configuration for API keys.
/// </summary>
public class AuthConfiguration
{
    public bool RequireAuthentication { get; set; } = false;
    public List<ApiKeyConfig> ApiKeys { get; set; } = new();
    public string HeaderName { get; set; } = "X-TaskForge-Key";
}
