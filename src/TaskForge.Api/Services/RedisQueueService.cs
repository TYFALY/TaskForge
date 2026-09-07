using System.Text.Json;
using StackExchange.Redis;
using TaskForge.Core;

namespace TaskForge.Api.Services;

/// <summary>
/// Interface for Redis queue operations.
/// </summary>
public interface IRedisQueueService
{
    /// <summary>
    /// Pushes a job envelope to the specified Redis queue.
    /// </summary>
    /// <param name="queueName">The queue name.</param>
    /// <param name="job">The job envelope to push.</param>
    /// <returns>True if the job was pushed successfully.</returns>
    Task<bool> PushJobAsync(string queueName, JobEnvelope job);

    /// <summary>
    /// Gets the size of the specified queue.
    /// </summary>
    /// <param name="queueName">The queue name.</param>
    /// <returns>The number of jobs in the queue.</returns>
    Task<long> GetQueueSizeAsync(string queueName);

    /// <summary>
    /// Gets the number of active workers from heartbeats.
    /// </summary>
    /// <returns>The number of active workers.</returns>
    Task<int> GetActiveWorkerCountAsync();

    /// <summary>
    /// Checks if the Redis connection is healthy.
    /// </summary>
    /// <returns>True if connected.</returns>
    bool IsConnected { get; }
}

/// <summary>
/// Service for interacting with Redis queues.
/// </summary>
public class RedisQueueService : IRedisQueueService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisQueueService> _logger;

    /// <summary>
    /// Initializes a new instance of the RedisQueueService.
    /// </summary>
    /// <param name="connectionMultiplexer">Redis connection multiplexer.</param>
    /// <param name="logger">Logger instance.</param>
    public RedisQueueService(IConnectionMultiplexer connectionMultiplexer, ILogger<RedisQueueService> logger)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsConnected => _connectionMultiplexer.IsConnected;

    /// <inheritdoc/>
    public async Task<bool> PushJobAsync(string queueName, JobEnvelope job)
    {
        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            var key = GetQueueKey(queueName);
            var serialized = JsonSerializer.Serialize(job);
            
            // LPUSH adds the job to the head (left) of the list.
            // Workers use BRPOP to consume from the tail (right).
            var result = await db.ListLeftPushAsync(key, serialized);
            
            // Notify waiting workers via pub/sub for event-driven wakeup
            var notificationChannel = new RedisChannel(GetNotificationChannel(queueName), RedisChannel.PatternMode.Literal);
            await db.PublishAsync(notificationChannel, job.Id.ToString());
            
            _logger.LogDebug("Job {JobId} pushed to Redis queue {QueueName}. New queue length: {Length}", 
                job.Id, queueName, result);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push job {JobId} to Redis queue {QueueName}", job.Id, queueName);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<long> GetQueueSizeAsync(string queueName)
    {
        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            var key = GetQueueKey(queueName);
            return await db.ListLengthAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get queue size for {QueueName}", queueName);
            return 0;
        }
    }

    /// <inheritdoc/>
    public async Task<int> GetActiveWorkerCountAsync()
    {
        try
        {
            // Use SCAN to iterate over worker heartbeat keys.
            // Pattern: taskforge:workers:*
            var endpoints = _connectionMultiplexer.GetEndPoints();
            var db = _connectionMultiplexer.GetDatabase();
            int count = 0;

            foreach (var endpoint in endpoints)
            {
                var server = _connectionMultiplexer.GetServer(endpoint);
                
                // Skip if server is not connected or doesn't support keyspace notifications
                if (!server.IsConnected || server.IsReplica)
                    continue;

                await foreach (var key in server.KeysAsync(pattern: "taskforge:workers:*"))
                {
                    var ttl = await db.KeyTimeToLiveAsync(key);
                    // Only count keys with positive TTL (active heartbeats)
                    if (ttl.HasValue && ttl.Value > TimeSpan.Zero)
                    {
                        count++;
                    }
                }
            }
            
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active worker count");
            return 0;
        }
    }

    /// <summary>
    /// Gets the Redis key for a queue.
    /// </summary>
    private static string GetQueueKey(string queueName) => $"taskforge:queue:{queueName}";

    /// <summary>
    /// Gets the Redis pub/sub channel for queue notifications.
    /// </summary>
    private static string GetNotificationChannel(string queueName) => $"taskforge:notify:{queueName}";
}