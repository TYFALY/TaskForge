using System.Text.Json;
using System.Threading.Channels;
using TaskForge.Api.Services.Embedded;
using TaskForge.Core;

namespace TaskForge.Api.Services;

public interface IJobBufferService
{
    int Count { get; }
    ChannelReader<JobEnvelope> Reader { get; }
    ValueTask<bool> EnqueueAsync(JobEnvelope job, CancellationToken cancellationToken = default);
    ValueTask<(bool Success, JobEnvelope? Job)> TryDequeueAsync(CancellationToken cancellationToken = default);
    ValueTask<JobEnvelope?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    IReadOnlyList<JobEnvelope> GetAllJobs();
    void UpdateJobStatus(Guid jobId, JobStatus status);
}

public class JobBufferService : IJobBufferService
{
    private const int MaxJobStoreSize = 5000;
    
    private readonly Channel<JobEnvelope> _channel;
    private readonly ILogger<JobBufferService> _logger;
    private readonly IJobEventBroadcaster _broadcaster;
    private readonly IEmbeddedJobRepository? _repository;
    private readonly Dictionary<Guid, JobEnvelope> _jobStore = new();
    private readonly LinkedList<Guid> _accessOrder = new();
    private readonly object _lock = new();

    public JobBufferService(ILogger<JobBufferService> logger, IJobEventBroadcaster broadcaster)
        : this(logger, broadcaster, repository: null)
    {
    }

    public JobBufferService(
        ILogger<JobBufferService> logger,
        IJobEventBroadcaster broadcaster,
        IEmbeddedJobRepository? repository)
    {
        _logger = logger;
        _broadcaster = broadcaster;
        _repository = repository;
        _channel = Channel.CreateUnbounded<JobEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public int Count => _channel.Reader.Count;
    public ChannelReader<JobEnvelope> Reader => _channel.Reader;

    public async ValueTask<bool> EnqueueAsync(JobEnvelope job, CancellationToken cancellationToken = default)
    {
        try
        {
            var jobWithStatus = job with { Status = JobStatus.Queued };
            lock (_lock)
            {
                // Evict oldest completed/failed jobs if at capacity
                while (_jobStore.Count >= MaxJobStoreSize)
                {
                    if (_accessOrder.First != null)
                    {
                        var oldestId = _accessOrder.First.Value;
                        _accessOrder.RemoveFirst();
                        _jobStore.Remove(oldestId);
                        _logger.LogDebug("[BUFFER] Evicted job {JobId} due to capacity limit", oldestId);
                    }
                    else break;
                }
                
                _jobStore[job.Id] = jobWithStatus;
                _accessOrder.AddLast(job.Id);
                _logger.LogDebug("[BUFFER] Enqueued job {JobId}, status=Queued, store count={Count}", job.Id, _jobStore.Count);
            }
            await _channel.Writer.WriteAsync(jobWithStatus, cancellationToken);

            await PersistJobAsync(jobWithStatus, cancellationToken);

            _broadcaster.BroadcastJobEnqueued(jobWithStatus);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to buffer job {JobId}", job.Id);
            return false;
        }
    }

    public async ValueTask<(bool Success, JobEnvelope? Job)> TryDequeueAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_channel.Reader.TryRead(out var dequeuedJob))
                {
                    lock (_lock)
                    {
                        if (_jobStore.TryGetValue(dequeuedJob.Id, out var job))
                        {
                            var updatedJob = job with { Status = JobStatus.Processing };
                            _jobStore[dequeuedJob.Id] = updatedJob;
                            
                            // Move to end of LRU list (most recently dequeued)
                            _accessOrder.Remove(dequeuedJob.Id);
                            _accessOrder.AddLast(dequeuedJob.Id);
                            
                            _logger.LogDebug("[BUFFER] Dequeued job {JobId}, status=Processing", dequeuedJob.Id);

                            _broadcaster.BroadcastJobStatusChanged(dequeuedJob.Id, JobStatus.Processing);

                            _ = PersistJobAsync(updatedJob, CancellationToken.None);

                            return (true, updatedJob);
                        }
                        else
                        {
                            _logger.LogWarning("[BUFFER] Job {JobId} not found in store during dequeue!", dequeuedJob.Id);
                        }
                    }
                }
            }
            return (false, null);
        }
        catch (OperationCanceledException)
        {
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dequeue job from buffer");
            return (false, null);
        }
    }

    public ValueTask<JobEnvelope?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_jobStore.TryGetValue(jobId, out var job))
            {
                _logger.LogDebug("[BUFFER] GetJob {JobId}, status={Status}", jobId, job.Status);
                return new ValueTask<JobEnvelope?>(job);
            }
        }
        _logger.LogWarning("[BUFFER] GetJob {JobId} NOT FOUND", jobId);
        return new ValueTask<JobEnvelope?>((JobEnvelope?)null);
    }

    public IReadOnlyList<JobEnvelope> GetAllJobs()
    {
        lock (_lock)
        {
            return _jobStore.Values
                .OrderByDescending(j => j.EnqueuedAt)
                .Take(100)
                .ToList();
        }
    }

    public void UpdateJobStatus(Guid jobId, JobStatus status)
    {
        lock (_lock)
        {
            if (_jobStore.TryGetValue(jobId, out var job))
            {
                _jobStore[jobId] = job with { Status = status };
                _logger.LogDebug("[BUFFER] UpdateJobStatus {JobId} -> {Status}", jobId, status);

                _broadcaster.BroadcastJobStatusChanged(jobId, status);

                _ = PersistJobAsync(_jobStore[jobId], CancellationToken.None);
            }
            else
            {
                _logger.LogWarning("[BUFFER] UpdateJobStatus {JobId} NOT FOUND!", jobId);
            }
        }
    }

    private async Task PersistJobAsync(JobEnvelope job, CancellationToken cancellationToken)
    {
        if (_repository == null)
        {
            return;
        }

        try
        {
            var entity = new JobEntity
            {
                Id = job.Id,
                QueueName = job.QueueName,
                PayloadJson = JsonSerializer.Serialize(job),
                Status = job.Status.ToString(),
                RetryCount = 0,
                MaxRetries = 3,
                CreatedAt = job.EnqueuedAt,
                UpdatedAt = DateTime.UtcNow
            };

            var existing = await _repository.GetJobAsync(job.Id, cancellationToken);
            if (existing == null)
            {
                await _repository.SaveJobAsync(entity, cancellationToken);
            }
            else
            {
                await _repository.UpdateJobAsync(entity, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist job {JobId} to embedded repository", job.Id);
        }
    }
}
