using TaskForge.Core;
using TaskForge.Worker.Services;

namespace TaskForge.Worker;

/// <summary>
/// Main background worker that polls the Redis queue and processes jobs.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IRedisQueueConsumer _consumer;
    private readonly IPostgresJobRepository _repository;
    private readonly WorkerHeartbeatService _heartbeat;
    private readonly JobProcessor _processor;

    private readonly string _queueName;
    private readonly int _pollingIntervalMs;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration,
        IRedisQueueConsumer consumer,
        IPostgresJobRepository repository,
        WorkerHeartbeatService heartbeat,
        JobProcessor processor)
    {
        _logger = logger;
        _configuration = configuration;
        _consumer = consumer;
        _repository = repository;
        _heartbeat = heartbeat;
        _processor = processor;

        _queueName = _configuration["Worker:QueueName"] ?? "default";
        _pollingIntervalMs = int.Parse(_configuration["Worker:PollingIntervalMs"] ?? "1000");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started. Queue: {QueueName}, WorkerId: {WorkerId}",
            _queueName, _heartbeat.WorkerId);

        // Start heartbeat in background
        var heartbeatTask = _heartbeat.StartAsync(stoppingToken);

        // Initialize database schema
        try
        {
            await _repository.InitializeSchemaAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize database schema. Will continue without DB.");
        }

        // Main polling loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _consumer.DequeueJobAsync(_queueName, stoppingToken);

                if (job != null)
                {
                    // Save job to database on first encounter
                    if (job.CurrentRetry == 0)
                    {
                        var entity = new JobEntity
                        {
                            Id = job.Id,
                            QueueName = job.QueueName,
                            PayloadJson = job.Payload,
                            Status = JobStatus.Queued.ToString(),
                            RetryCount = 0,
                            MaxRetries = job.MaxRetries,
                            CreatedAt = job.EnqueuedAt,
                            UpdatedAt = DateTime.UtcNow
                        };
                        await _repository.SaveJobAsync(entity, stoppingToken);
                    }

                    await _processor.ProcessJobAsync(job, stoppingToken);
                }
                else
                {
                    // Queue empty, wait before next poll
                    await Task.Delay(_pollingIntervalMs, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in worker polling loop");
                await Task.Delay(1000, stoppingToken);
            }
        }

        await _heartbeat.StopAsync(stoppingToken);
        _logger.LogInformation("Worker stopped. WorkerId: {WorkerId}", _heartbeat.WorkerId);
    }
}
