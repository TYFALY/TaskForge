using TaskForge.Core;

namespace TaskForge.Worker.Services;

/// <summary>
/// Reclaims jobs stuck in PROCESSING state when their leasing worker
/// has failed (missed heartbeats). Uses SELECT ... FOR UPDATE SKIP LOCKED
/// to safely atomically re-queue without double-processing.
/// </summary>
public class OrphanedJobRecoveryService : BackgroundService
{
    private readonly IPostgresJobRepository _repository;
    private readonly IRedisQueueConsumer _consumer;
    private readonly IWorkerHeartbeatService _heartbeat;
    private readonly ILogger<OrphanedJobRecoveryService> _logger;
    private readonly TimeSpan _leaseExpiry;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    public OrphanedJobRecoveryService(
        IPostgresJobRepository repository,
        IRedisQueueConsumer consumer,
        IWorkerHeartbeatService heartbeat,
        ILogger<OrphanedJobRecoveryService> logger,
        TimeSpan? leaseExpiry = null)
    {
        _repository = repository;
        _consumer = consumer;
        _heartbeat = heartbeat;
        _logger = logger;
        _leaseExpiry = leaseExpiry ?? TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ORPHAN-RECOVERY] Started orphaned job recovery (leaseExpiry={Expiry}, pollInterval={Interval})",
            _leaseExpiry, _pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverOrphanedJobsAsync(stoppingToken);
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ORPHAN-RECOVERY] Error in recovery loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task RecoverOrphanedJobsAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.Subtract(_leaseExpiry);
        var orphans = await _repository.GetOrphanedJobsAsync(cutoff, ct);

        if (orphans.Count == 0) return;

        _logger.LogWarning("[ORPHAN-RECOVERY] Found {Count} orphaned jobs to recover", orphans.Count);

        foreach (var job in orphans)
        {
            try
            {
                // Verify the worker is truly dead via heartbeat
                if (job.LockedBy.HasValue)
                {
                    var workerAlive = await _heartbeat.IsWorkerAliveAsync(job.LockedBy.Value, ct);
                    if (workerAlive)
                    {
                        _logger.LogDebug("[ORPHAN-RECOVERY] Worker {WorkerId} for job {JobId} is still alive, skipping", job.LockedBy, job.Id);
                        continue;
                    }
                }

                // Atomically reclaim: reset to Queued with fresh lease
                await _repository.ResetJobToQueuedAsync(job.Id, ct);
                _logger.LogInformation("[ORPHAN-RECOVERY] Reclaimed job {JobId} from dead worker {WorkerId}", job.Id, job.LockedBy);

                // Re-enqueue to Redis - create fresh envelope from entity data
                var envelope = new JobEnvelope
                {
                    Id = job.Id,
                    QueueName = job.QueueName,
                    Payload = job.PayloadJson,
                    Status = JobStatus.Queued,
                    CurrentRetry = job.RetryCount,
                    MaxRetries = job.MaxRetries
                };
                await _consumer.RequeueJobAsync(envelope, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ORPHAN-RECOVERY] Failed to recover job {JobId}", job.Id);
            }
        }
    }
}
