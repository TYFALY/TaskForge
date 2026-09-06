using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using TaskForge.Core;

namespace TaskForge.Api.Services;

public interface IJobEventBroadcaster
{
    void BroadcastJobEnqueued(JobEnvelope job);
    void BroadcastJobStatusChanged(Guid jobId, JobStatus status);
    IAsyncEnumerable<JobEvent> SubscribeAsync(CancellationToken cancellationToken = default);
}

public record JobEvent(string EventType, Guid JobId, JobStatus Status, string? Data, DateTime Timestamp);

public class JobEventBroadcaster : IJobEventBroadcaster
{
    private readonly ConcurrentDictionary<string, Channel<JobEvent>> _subscribers = new();
    private readonly ILogger<JobEventBroadcaster> _logger;

    public JobEventBroadcaster(ILogger<JobEventBroadcaster> logger)
    {
        _logger = logger;
    }

    public void BroadcastJobEnqueued(JobEnvelope job)
    {
        var jobData = JsonSerializer.Serialize(new
        {
            id = job.Id,
            queueName = job.QueueName,
            payload = job.Payload,
            status = job.Status.ToString(),
            jobType = job.JobType.ToString(),
            retryCount = job.CurrentRetry,
            maxRetries = job.MaxRetries,
            createdAt = job.EnqueuedAt,
            updatedAt = job.EnqueuedAt
        });

        var evt = new JobEvent("job_enqueued", job.Id, job.Status, jobData, DateTime.UtcNow);
        Broadcast(evt);
        _logger.LogDebug("[SSE] Broadcasted job_enqueued for {JobId}", job.Id);
    }

    public void BroadcastJobStatusChanged(Guid jobId, JobStatus status)
    {
        var evt = new JobEvent("job_updated", jobId, status, null, DateTime.UtcNow);
        Broadcast(evt);
        _logger.LogDebug("[SSE] Broadcasted job_updated for {JobId} -> {Status}", jobId, status);
    }

    private void Broadcast(JobEvent evt)
    {
        foreach (var kvp in _subscribers)
        {
            try
            {
                if (!kvp.Value.Writer.TryWrite(evt))
                {
                    // Channel is full - subscriber is slow, log warning
                    _logger.LogWarning("[SSE] Subscriber {SubscriberId} channel is full, event may be dropped", kvp.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SSE] Failed to write event to subscriber {SubscriberId}", kvp.Key);
            }
        }
    }

    public async IAsyncEnumerable<JobEvent> SubscribeAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscriberId = Guid.NewGuid().ToString("N")[..8];
        var channel = Channel.CreateUnbounded<JobEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _subscribers.TryAdd(subscriberId, channel);
        _logger.LogInformation("[SSE] New subscriber connected: {SubscriberId} (total: {Count})", subscriberId, _subscribers.Count);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            // Complete the channel writer and remove subscriber
            if (_subscribers.TryRemove(subscriberId, out var removedChannel))
            {
                removedChannel.Writer.TryComplete();
            }
            _logger.LogInformation("[SSE] Subscriber disconnected: {SubscriberId} (remaining: {Count})", subscriberId, _subscribers.Count);
        }
    }
}
