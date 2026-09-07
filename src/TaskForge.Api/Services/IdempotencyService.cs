using StackExchange.Redis;
using TaskForge.Core;

namespace TaskForge.Api.Services;

/// <summary>
/// Implements Idempotency-Key support using Redis as the backing store.
/// Keys expire after 24 hours to prevent unbounded growth.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Gets the cached job ID for an idempotency key, or creates and caches a new one.
    /// </summary>
    Task<Guid> GetOrCachedJobIdAsync(string idempotencyKey, JobType jobType, Func<Task<Guid>> createJobIdFactory);
}

public class IdempotencyService : IIdempotencyService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IdempotencyService> _logger;
    private static readonly TimeSpan KeyTtl = TimeSpan.FromHours(24);

    public IdempotencyService(IConnectionMultiplexer redis, ILogger<IdempotencyService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<Guid> GetOrCachedJobIdAsync(string idempotencyKey, JobType jobType, Func<Task<Guid>> createJobIdFactory)
    {
        var db = _redis.GetDatabase();
        var key = GetRedisKey(idempotencyKey);

        // Try to get existing job ID
        var existing = await db.StringGetAsync(key);
        if (!existing.IsNull)
        {
            _logger.LogDebug("Idempotency key {Key} maps to existing job {JobId}", idempotencyKey, existing);
            return Guid.Parse(existing.ToString());
        }

        // Create new job ID
        var newId = await createJobIdFactory();

        // Atomically set if not already present (NX = only if not exists)
        var wasSet = await db.StringSetAsync(key, newId.ToString(), KeyTtl, When.NotExists);
        if (wasSet)
        {
            _logger.LogDebug("Cached new job {JobId} for idempotency key {Key}", newId, idempotencyKey);
            return newId;
        }

        // Another request already set it first - fetch and return
        var raceResult = await db.StringGetAsync(key);
        _logger.LogDebug("Race won by another request for idempotency key {Key}", idempotencyKey);
        return Guid.Parse(raceResult.ToString());
    }

    private static string GetRedisKey(string idempotencyKey) => $"taskforge:idempotency:{idempotencyKey}";
}
