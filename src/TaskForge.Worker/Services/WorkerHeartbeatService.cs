using StackExchange.Redis;

namespace TaskForge.Worker.Services;

/// <summary>
/// Interface for the worker heartbeat service.
/// </summary>
public interface IWorkerHeartbeatService
{
    /// <summary>
    /// Gets the unique worker ID.
    /// </summary>
    Guid WorkerId { get; }

    /// <summary>
    /// Starts the heartbeat background task.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the heartbeat background task.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a worker is alive (has a valid heartbeat).
    /// </summary>
    Task<bool> IsWorkerAliveAsync(Guid workerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Background service that sends heartbeats to Redis every 3 seconds with a 10-second TTL.
/// </summary>
public class WorkerHeartbeatService : IWorkerHeartbeatService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<WorkerHeartbeatService> _logger;
    private readonly int _intervalSeconds;
    private readonly int _ttlSeconds;
    private CancellationTokenSource? _cts;

    public Guid WorkerId { get; } = Guid.NewGuid();

    public WorkerHeartbeatService(IConnectionMultiplexer redis, ILogger<WorkerHeartbeatService> logger, int intervalSeconds = 3, int ttlSeconds = 10)
    {
        _redis = redis;
        _logger = logger;
        _intervalSeconds = intervalSeconds;
        _ttlSeconds = ttlSeconds;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _logger.LogInformation("Worker heartbeat started. WorkerId: {WorkerId}", WorkerId);
        await SendHeartbeatAsync();

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), _cts.Token);
                await SendHeartbeatAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error sending worker heartbeat"); }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _logger.LogInformation("Worker heartbeat stopped. WorkerId: {WorkerId}", WorkerId);
        return Task.CompletedTask;
    }

    private async Task SendHeartbeatAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"taskforge:workers:{WorkerId}";
            await db.StringSetAsync(key, DateTime.UtcNow.ToString("O"), TimeSpan.FromSeconds(_ttlSeconds));
            _logger.LogDebug("Heartbeat sent for worker {WorkerId}, TTL: {TTL}s", WorkerId, _ttlSeconds);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send heartbeat for worker {WorkerId}", WorkerId); }
    }

    public async Task<bool> IsWorkerAliveAsync(Guid workerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"taskforge:workers:{workerId}";
            var exists = await db.KeyExistsAsync(key);
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check heartbeat for worker {WorkerId}", workerId);
            return false;
        }
    }
}