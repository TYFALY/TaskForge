using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using TaskForge.Core.Auth;
using TaskForge.Core.RateLimiting;

namespace TaskForge.Api.Middleware;

public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;
    private readonly AuthConfiguration _authConfig;
    private readonly Dictionary<string, ApiKeyConfig> _apiKeys;
    private readonly Dictionary<string, TokenBucketRateLimiter> _rateLimiters;
    private readonly Dictionary<string, SemaphoreSlim> _queueSemaphores;
    private readonly bool _requireAuth;

    public ApiKeyAuthMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyAuthMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        
        // Load auth configuration
        _authConfig = configuration.GetSection("Auth").Get<AuthConfiguration>() ?? new AuthConfiguration();
        
        // Check if auth is required (default to true if any API keys are configured)
        _requireAuth = _authConfig.RequireAuthentication;
        
        // Check for environment variable API key
        var envApiKey = Environment.GetEnvironmentVariable("TaskForge__ApiKey") 
                      ?? Environment.GetEnvironmentVariable("TASKFORGE_API_KEY");
        
        // Build API keys dictionary
        _apiKeys = new Dictionary<string, ApiKeyConfig>();
        
        // Add keys from configuration
        foreach (var apiKey in _authConfig.ApiKeys)
        {
            _apiKeys[apiKey.Key] = apiKey;
        }
        
        // Add key from environment variable (if present)
        if (!string.IsNullOrEmpty(envApiKey))
        {
            var envKeyConfig = new ApiKeyConfig
            {
                Key = envApiKey,
                Name = "Environment API Key",
                Namespace = "default",
                IsActive = true,
                RateLimitPerSecond = 100
            };
            _apiKeys[envApiKey] = envKeyConfig;
            _logger.LogInformation("[AUTH] Loaded API key from environment variable");
        }
        
        // Initialize rate limiters for all keys
        _rateLimiters = new Dictionary<string, TokenBucketRateLimiter>();
        _queueSemaphores = new Dictionary<string, SemaphoreSlim>();
        
        foreach (var kvp in _apiKeys)
        {
            var key = GetKeyId(kvp.Key);
            _rateLimiters[key] = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = kvp.Value.RateLimitPerSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                TokensPerPeriod = kvp.Value.RateLimitPerSecond,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
        }
        
        _logger.LogInformation("[AUTH] ApiKeyAuthMiddleware initialized. Keys loaded: {KeyCount}, Auth required: {RequireAuth}", 
            _apiKeys.Count, _requireAuth);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for metrics endpoint and static files
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path.StartsWith("/metrics") || path.StartsWith("/api/v1/metrics") ||
            path.StartsWith("/swagger") || path.StartsWith("/health") ||
            path.EndsWith(".html") || path.EndsWith(".js") || path.EndsWith(".css") ||
            path.EndsWith(".png") || path.EndsWith(".ico") || path.EndsWith(".svg"))
        {
            await _next(context);
            return;
        }

        // Skip if auth not required and no keys are configured
        if (!_requireAuth && _apiKeys.Count == 0)
        {
            await _next(context);
            return;
        }

        // If auth is not required but keys exist, allow requests without keys
        if (!_requireAuth && _apiKeys.Count > 0)
        {
            // Check if request has a key - if so, validate it
            var headerName = _authConfig.HeaderName;
            if (context.Request.Headers.TryGetValue(headerName, out var apiKeyHeader) 
                && !string.IsNullOrEmpty(apiKeyHeader.ToString()))
            {
                var apiKey = apiKeyHeader.ToString();
                if (_apiKeys.TryGetValue(apiKey, out var keyConfig) && keyConfig.IsActive)
                {
                    // Apply rate limiting for authenticated requests
                    var limiterKey = GetKeyId(apiKey);
                    if (_rateLimiters.TryGetValue(limiterKey, out var rateLimiter))
                    {
                        using var lease = await rateLimiter.AcquireAsync(1);
                        if (!lease.IsAcquired)
                        {
                            context.Response.StatusCode = 429;
                            context.Response.Headers["Retry-After"] = "1";
                            await context.Response.WriteAsJsonAsync(new
                            {
                                error = "Rate limit exceeded",
                                code = "RATE_LIMITED",
                                retryAfter = 1,
                                limit = keyConfig.RateLimitPerSecond
                            });
                            return;
                        }
                    }
                    
                    context.Items["ApiKeyConfig"] = keyConfig;
                    context.Items["Namespace"] = keyConfig.Namespace;
                    context.Items["TenantId"] = keyConfig.TenantId;
                }
            }
            
            // Allow unauthenticated requests through
            await _next(context);
            return;
        }

        // Auth is required
        var authHeaderName = _authConfig.HeaderName;
        if (!context.Request.Headers.TryGetValue(authHeaderName, out var authHeader))
        {
            _logger.LogWarning("Missing API key header: {Header}", authHeaderName);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing API key", code = "AUTH_REQUIRED" });
            return;
        }

        var apiKeyValue = authHeader.ToString();
        if (!_apiKeys.TryGetValue(apiKeyValue, out var config) || !config.IsActive)
        {
            _logger.LogWarning("Invalid or inactive API key attempted");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key", code = "AUTH_INVALID" });
            return;
        }

        // Apply rate limiting
        var limiterKeyForAuth = GetKeyId(apiKeyValue);
        if (_rateLimiters.TryGetValue(limiterKeyForAuth, out var rateLimiterForAuth))
        {
            using var lease = await rateLimiterForAuth.AcquireAsync(1);
            if (!lease.IsAcquired)
            {
                _logger.LogWarning("Rate limit exceeded for key: {KeyName}", config.Name);
                context.Response.StatusCode = 429;
                context.Response.Headers["Retry-After"] = "1";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit exceeded",
                    code = "RATE_LIMITED",
                    retryAfter = 1,
                    limit = config.RateLimitPerSecond
                });
                return;
            }
        }

        // Store context for later use
        context.Items["ApiKeyConfig"] = config;
        context.Items["Namespace"] = config.Namespace;
        context.Items["TenantId"] = config.TenantId;

        _logger.LogDebug("Request authenticated for: {KeyName} in namespace: {Namespace}",
            config.Name, config.Namespace);

        await _next(context);
    }

    public SemaphoreSlim GetQueueSemaphore(string queueName, string @namespace, int maxConcurrency)
    {
        var key = $"{@namespace}:{queueName}";
        lock (_queueSemaphores)
        {
            if (!_queueSemaphores.TryGetValue(key, out var semaphore))
            {
                semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
                _queueSemaphores[key] = semaphore;
            }
            return semaphore;
        }
    }

    private static string GetKeyId(string key) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
}

public static class ApiKeyAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<ApiKeyAuthMiddleware>();
}
