using TaskForge.Core;

namespace TaskForge.Api.Services;

public class BufferProcessorService : BackgroundService
{
    private readonly IJobBufferService _bufferService;
    private readonly IJobQueueService _queueService;
    private readonly ILogger<BufferProcessorService> _logger;

    public BufferProcessorService(
        IJobBufferService bufferService,
        IJobQueueService queueService,
        ILogger<BufferProcessorService> logger)
    {
        _bufferService = bufferService;
        _queueService = queueService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Buffer processor service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (success, job) = await _bufferService.TryDequeueAsync(stoppingToken);
                if (success && job != null)
                {
                    await ProcessJobAsync(job, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing job from buffer");
                await Task.Delay(100, stoppingToken);
            }
        }

        _logger.LogInformation("Buffer processor service stopped");
    }

    private async Task ProcessJobAsync(JobEnvelope job, CancellationToken cancellationToken)
    {
        var success = await _queueService.PushJobAsync(job.QueueName, job);
        if (success)
            _logger.LogInformation("Job {JobId} pushed to queue {QueueName}", job.Id, job.QueueName);
        else
            _logger.LogError("Failed to push job {JobId} to queue {QueueName}", job.Id, job.QueueName);
    }
}