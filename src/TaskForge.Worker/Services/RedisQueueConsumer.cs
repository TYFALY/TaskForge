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

            // BRPOP - Block right pop from list (FIFO queue behavior)
            // Returns the job ID from the tail of the list
            var result = await db.ListRightPopAsync(key);

            if (result.IsNullOrEmpty)
            {
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

    private static string GetQueueKey(string queueName) => $"taskforge:queue:{queueName}";
}