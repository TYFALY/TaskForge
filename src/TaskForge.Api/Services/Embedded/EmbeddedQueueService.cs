using System.Collections.Concurrent;
using System.Text.Json;
using TaskForge.Core;

namespace TaskForge.Api.Services.Embedded;

public interface IEmbeddedQueueService
{
    Task<bool> PushJobAsync(string queueName, JobEnvelope job);
    Task<JobEnvelope?> PopJobAsync(string queueName);
    Task<long> GetQueueSizeAsync(string queueName);
    Task<int> GetActiveWorkerCountAsync();
    bool IsEmbeddedMode { get; }
    void RegisterWorker(string workerId);
    void UnregisterWorker(string workerId);
}

public class EmbeddedQueueService : IEmbeddedQueueService, IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<JobEnvelope>> _queues = new();
    private readonly ConcurrentDictionary<string, byte> _activeWorkers = new();
    private readonly ILogger<EmbeddedQueueService> _logger;
    private bool _disposed;
    
    public EmbeddedQueueService(ILogger<EmbeddedQueueService> logger)
    {
        _logger = logger;
        _logger.LogInformation("Embedded queue service initialized");
    }
    
    public bool IsEmbeddedMode => true;
    
    public Task<bool> PushJobAsync(string queueName, JobEnvelope job)
    {
        try
        {
            var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<JobEnvelope>());
            queue.Enqueue(job);
            _logger.LogDebug("Job {JobId} pushed to embedded queue {QueueName}. Queue size: {Size}", 
                job.Id, queueName, queue.Count);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push job {JobId} to embedded queue {QueueName}", job.Id, queueName);
            return Task.FromResult(false);
        }
    }
    
    public Task<JobEnvelope?> PopJobAsync(string queueName)
    {
        try
        {
            if (_queues.TryGetValue(queueName, out var queue) && queue.TryDequeue(out var job))
            {
                _logger.LogDebug("Job {JobId} popped from embedded queue {QueueName}", job.Id, queueName);
                return Task.FromResult<JobEnvelope?>(job);
            }
            return Task.FromResult<JobEnvelope?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pop job from embedded queue {QueueName}", queueName);
            return Task.FromResult<JobEnvelope?>(null);
        }
    }
    
    public Task<long> GetQueueSizeAsync(string queueName)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult<long>(queue.Count);
        }
        return Task.FromResult<long>(0);
    }
    
    public Task<int> GetActiveWorkerCountAsync()
    {
        return Task.FromResult(_activeWorkers.Count);
    }
    
    public void RegisterWorker(string workerId)
    {
        _activeWorkers.TryAdd(workerId, 1);
    }
    
    public void UnregisterWorker(string workerId)
    {
        _activeWorkers.TryRemove(workerId, out _);
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _queues.Clear();
            _activeWorkers.Clear();
            _disposed = true;
        }
    }
}
