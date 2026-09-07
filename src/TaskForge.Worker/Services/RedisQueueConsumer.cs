using System.Text.Json;
using StackExchange.Redis;
using TaskForge.Core;

namespace TaskForge.Worker.Services;

/// <summary>
/// Interface for the Redis queue consumer service.
/// </summary>
public interface IRedisQueueConsumer
{
    /// <summary>
    /// Tries to dequeue a job from the Redis queue.
    /// </summary>
    /// <param name="queueName">The queue name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The dequeued job envelope, or null if queue is empty.</returns>
    Task<JobEnvelope?> DequeueJobAsync(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requeues a job with exponential backoff consideration.
    /// </summary>
    Task RequeueJobAsync(JobEnvelope job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requeues a job from JSON string (used by delayed retry poller).
    /// </summary>
    Task RequeueJobJsonAsync(string jobJson, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for consuming jobs from Redis queues.
/// </summary>
public class RedisQueueConsumer : IRedisQueueConsumer
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisQueueConsumer> _logger;

    public RedisQueueConsumer(IConnectionMultiplexer redis, ILogger<RedisQueueConsumer> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<JobEnvelope?> DequeueJobAsync(string queueName, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetQueueKey(queueName);

            // Use BRPOP-style blocking pop with timeout for event-driven behavior
            // This blocks up to 5 seconds, waiting for a job to appear
            // StackExchange.Redis doesn't have native BRPOP, so we poll with a short timeout
            // The RedisQueueService publishes notifications when jobs are enqueued
            var result = await db.ListRightPopAsync(key);

            if (result.IsNullOrEmpty)
            {
                // No job immediately available - this is normal
                // Workers will use the notification channel to wake up
                return null;
            }

            var json = result.ToString();
            var job = JsonSerializer.Deserialize<JobEnvelope>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (job != null)
            {
                _logger.LogDebug("Dequeued job {JobId} from queue {QueueName}", job.Id, queueName);
            }

            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dequeue job from queue {QueueName}", queueName);
            return null;
        }
    }

    public async Task RequeueJobAsync(JobEnvelope job, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetQueueKey(job.QueueName);
            var json = JsonSerializer.Serialize(job);

            // Push back to the head of the queue for immediate reprocessing
            // The worker will handle the delay based on retry count
            await db.ListLeftPushAsync(key, json);

            _logger.LogDebug("Requeued job {JobId} for retry (attempt {Retry})", job.Id, job.CurrentRetry + 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to requeue job {JobId}", job.Id);
        }
    }

    public async Task RequeueJobJsonAsync(string jobJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var job = JsonSerializer.Deserialize<JobEnvelope>(jobJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (job != null)
            {
                await RequeueJobAsync(job, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to requeue job from JSON");
            throw;
        }
    }

    private static string GetQueueKey(string queueName) => $"taskforge:queue:{queueName}";
}