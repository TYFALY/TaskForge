using StackExchange.Redis;

namespace TaskForge.Worker.Services;

/// <summary>
/// Interface for delayed retry scheduling via Redis ZSET.
/// </summary>
public interface IDelayedRetryService
{
    /// <summary>
    /// Schedule a job for delayed retry.
    /// </summary>
    Task ScheduleRetryAsync(string jobJson, int delaySeconds, CancellationToken ct = default);
}

/// <summary>
/// Delayed retry poller that replaces thread-blocking Task.Delay with Redis ZSET.
/// Jobs are added to a sorted set with their next-retry timestamp as score.
/// A background loop polls every second and re-enqueues ready jobs.
/// </summary>
public class DelayedRetryService : BackgroundService, IDelayedRetryService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IRedisQueueConsumer _consumer;
    private readonly ILogger<DelayedRetryService> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);

    private const string DelayedSetKey = "taskforge:delayed";

    public DelayedRetryService(
        IConnectionMultiplexer redis,
        IRedisQueueConsumer consumer,
        ILogger<DelayedRetryService> logger)
    {
        _redis = redis;
        _consumer = consumer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DELAYED-RETRY] Started delayed retry poller");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessReadyJobsAsync(stoppingToken);
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DELAYED-RETRY] Error in retry poller loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("[DELAYED-RETRY] Stopped delayed retry poller");
    }

    /// <summary>
    /// Schedule a job for delayed retry using Redis ZSET.
    /// </summary>
    public async Task ScheduleRetryAsync(string jobJson, int delaySeconds, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var readyTime = DateTimeOffset.UtcNow.AddSeconds(delaySeconds).ToUnixTimeSeconds();
        await db.SortedSetAddAsync(DelayedSetKey, jobJson, readyTime);
        _logger.LogDebug("[DELAYED-RETRY] Scheduled job for retry in {Delay}s at {ReadyTime}", delaySeconds, readyTime);
    }

    private async Task ProcessReadyJobsAsync(CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Atomically grab all jobs ready to retry (score <= now)
        var jobs = await db.SortedSetRangeByScoreAsync(DelayedSetKey, 0, now);

        if (jobs.Length == 0) return;

        _logger.LogInformation("[DELAYED-RETRY] Found {Count} jobs ready for retry", jobs.Length);

        foreach (var jobJson in jobs)
        {
            try
            {
                // Remove from delayed set
                var removed = await db.SortedSetRemoveAsync(DelayedSetKey, jobJson);
                if (!removed) continue; // Already picked up by another poller

                // Re-enqueue to Redis stream
                await _consumer.RequeueJobJsonAsync(jobJson!, ct);
                _logger.LogDebug("[DELAYED-RETRY] Re-enqueued delayed job");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DELAYED-RETRY] Failed to re-enqueue delayed job: {Job}", jobJson);
            }
        }
    }
}
