using System.Threading.Channels;
using System.Threading.Tasks;
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
}

public class JobBufferService : IJobBufferService
{
    private readonly Channel<JobEnvelope> _channel;
    private readonly ILogger<JobBufferService> _logger;
    private readonly IJobEventBroadcaster _broadcaster;
    private readonly Dictionary<Guid, JobEnvelope> _jobStore = new();
    private readonly object _lock = new();

    public JobBufferService(ILogger<JobBufferService> logger, IJobEventBroadcaster broadcaster)
    {
        _logger = logger;
        _broadcaster = broadcaster;
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
                _jobStore[job.Id] = jobWithStatus;
                _logger.LogDebug("[BUFFER] Enqueued job {JobId}, status=Queued, store count={Count}", job.Id, _jobStore.Count);
            }
            await _channel.Writer.WriteAsync(job, cancellationToken);

            // Broadcast job enqueued event
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
                            // Create updated job with Processing status
                            var updatedJob = job with { Status = JobStatus.Processing };
                            _jobStore[dequeuedJob.Id] = updatedJob;
                            _logger.LogDebug("[BUFFER] Dequeued job {JobId}, status=Processing", dequeuedJob.Id);

                            // Broadcast status change to Processing
                            _broadcaster.BroadcastJobStatusChanged(dequeuedJob.Id, JobStatus.Processing);

                            // Return the UPDATED job, not the original (fixes SSE returning stale status)
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
            // Return recent jobs (last 100), sorted by creation time descending
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

                // Broadcast status change
                _broadcaster.BroadcastJobStatusChanged(jobId, status);
            }
            else
            {
                _logger.LogWarning("[BUFFER] UpdateJobStatus {JobId} NOT FOUND!", jobId);
            }
        }
    }
}