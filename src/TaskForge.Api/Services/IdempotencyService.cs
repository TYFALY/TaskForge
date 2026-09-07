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

        // Two-phase atomic reservation:
        // Phase 1: Try to claim with a "PROCESSING" state marker
        var processingKey = $"{key}:processing";
        var claimed = await db.StringSetAsync(processingKey, newId.ToString(), TimeSpan.FromSeconds(30), When.NotExists);
        
        if (claimed)
        {
            try
            {
                // Phase 2: Atomically set the actual key if still holding the reservation
                var wasSet = await db.StringSetAsync(key, newId.ToString(), KeyTtl, When.NotExists);
                if (wasSet)
                {
                    _logger.LogDebug("Cached new job {JobId} for idempotency key {Key}", newId, idempotencyKey);
                    return newId;
                }
                
                // Another request set the key first - fetch and return
                var raceResult = await db.StringGetAsync(key);
                _logger.LogDebug("Race won by another request for idempotency key {Key}", idempotencyKey);
                return Guid.Parse(raceResult.ToString());
            }
            finally
            {
                // Release the processing reservation
                await db.KeyDeleteAsync(processingKey);
            }
        }

        // Another request is already creating this job - wait briefly and fetch
        await Task.Delay(50);
        var result = await db.StringGetAsync(key);
        if (!result.IsNull)
        {
            return Guid.Parse(result.ToString());
        }

        // Edge case: first writer failed, try to claim again
        return await GetOrCachedJobIdAsync(idempotencyKey, jobType, createJobIdFactory);
    }

    private static string GetRedisKey(string idempotencyKey) => $"taskforge:idempotency:{idempotencyKey}";
}
