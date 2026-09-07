using TaskForge.Core;

namespace TaskForge.Api.Services;

/// <summary>
/// Clean adapter that wraps <see cref="IRedisQueueService"/> to satisfy the
/// mode-agnostic <see cref="IJobQueueService"/> contract used by the
/// distributed (Redis) deployment. Keeps the Redis-specific surface
/// (<see cref="IRedisQueueService"/>) intact while exposing a stable
/// abstraction to application services such as <see cref="BufferProcessorService"/>.
/// </summary>
public class RedisQueueServiceAdapter : IJobQueueService
{
    private readonly IRedisQueueService _redis;

    /// <summary>
    /// Creates a new instance of the adapter.
    /// </summary>
    /// <param name="redis">The Redis-backed queue service to delegate calls to.</param>
    public RedisQueueServiceAdapter(IRedisQueueService redis) => _redis = redis;

    /// <inheritdoc />
    public bool IsConnected => _redis.IsConnected;

    /// <inheritdoc />
    public bool IsEmbeddedMode => false;

    /// <inheritdoc />
    public Task<bool> PushJobAsync(string queueName, JobEnvelope job) =>
        _redis.PushJobAsync(queueName, job);

    /// <inheritdoc />
    public Task<long> GetQueueSizeAsync(string queueName) =>
        _redis.GetQueueSizeAsync(queueName);

    /// <inheritdoc />
    public Task<int> GetActiveWorkerCountAsync() =>
        _redis.GetActiveWorkerCountAsync();
}
