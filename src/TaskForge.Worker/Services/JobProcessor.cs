using System.Diagnostics;
using TaskForge.Core;
using TaskForge.Worker.Handlers;

namespace TaskForge.Worker.Services;

public class JobProcessor
{
    private readonly IPostgresJobRepository _repository;
    private readonly IRedisQueueConsumer _consumer;
    private readonly IWorkerHeartbeatService _heartbeat;
    private readonly IEnumerable<IJobHandler> _handlers;
    private readonly ILogger<JobProcessor> _logger;
    private readonly int _maxRetries;
    private readonly Dictionary<JobType, IJobHandler> _handlerMap;

    public JobProcessor(
        IPostgresJobRepository repository,
        IRedisQueueConsumer consumer,
        IWorkerHeartbeatService heartbeat,
        IEnumerable<IJobHandler> handlers,
        ILogger<JobProcessor> logger,
        int maxRetries = 3)
    {
        _repository = repository;
        _consumer = consumer;
        _heartbeat = heartbeat;
        _handlers = handlers;
        _logger = logger;
        _maxRetries = maxRetries;
        _handlerMap = handlers.ToDictionary(h => h.SupportedType);
    }

    public async Task ProcessJobAsync(JobEnvelope job, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Processing job {JobId} from queue {QueueName} (type: {JobType})",
            job.Id, job.QueueName, job.JobType);

        try
        {
            await _repository.UpdateJobStatusAsync(job.Id, JobStatus.Processing.ToString(), cancellationToken);

            var result = await ExecuteJobAsync(job, cancellationToken);
            stopwatch.Stop();

            if (result.Success)
            {
                await _repository.UpdateJobStatusAsync(job.Id, JobStatus.Completed.ToString(), cancellationToken);
                _logger.LogInformation("Job {JobId} completed successfully in {Duration}ms",
                    job.Id, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                await HandleJobFailureAsync(job, result.ErrorMessage ?? "Job execution failed", cancellationToken);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing job {JobId}", job.Id);
            await HandleJobFailureAsync(job, ex.Message, cancellationToken);
        }
    }

    private async Task<JobExecutionResult> ExecuteJobAsync(JobEnvelope job, CancellationToken cancellationToken)
    {
        if (_handlerMap.TryGetValue(job.JobType, out var handler))
        {
            return await handler.ExecuteAsync(job, cancellationToken);
        }

        _logger.LogWarning("No handler found for job type {JobType}, using default", job.JobType);
        await Task.Delay(100, cancellationToken);
        return JobExecutionResult.Succeeded();
    }

    private async Task HandleJobFailureAsync(JobEnvelope job, string reason, CancellationToken cancellationToken)
    {
        var newRetryCount = job.CurrentRetry + 1;

        if (newRetryCount >= _maxRetries)
        {
            _logger.LogWarning("Job {JobId} has exhausted {Retries} retries. Marking as DeadLettered.",
                job.Id, _maxRetries);

            await _repository.MarkAsDeadLetteredAsync(job.Id, reason, cancellationToken);
        }
        else
        {
            var delaySeconds = (int)Math.Pow(2, newRetryCount);
            _logger.LogInformation("Job {JobId} will be retried in {Delay}s (attempt {Retry}/{Max})",
                job.Id, delaySeconds, newRetryCount, _maxRetries);

            await _repository.IncrementRetryCountAsync(job.Id, cancellationToken);
            var updatedJob = job with { CurrentRetry = newRetryCount };

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            await _consumer.RequeueJobAsync(updatedJob, cancellationToken);
        }
    }
}
