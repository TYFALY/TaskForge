using Cronos;
using TaskForge.Core;

namespace TaskForge.Worker;

public class CronSchedulerService : BackgroundService
{
    private readonly ILogger<CronSchedulerService> _logger;
    private readonly Dictionary<string, CronScheduleEntry> _schedules = new();
    private readonly object _lock = new();

    public CronSchedulerService(ILogger<CronSchedulerService> logger)
    {
        _logger = logger;
    }

    public void RegisterSchedule(string name, string cronExpression, string queueName, string payload,
        string @namespace = "default", string? tenantId = null, int maxRetries = 3)
    {
        try
        {
            var cron = CronExpression.Parse(cronExpression);
            var nextOccurrence = cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);

            lock (_lock)
            {
                _schedules[name] = new CronScheduleEntry
                {
                    Name = name,
                    CronExpression = cronExpression,
                    QueueName = queueName,
                    Payload = payload,
                    Namespace = @namespace,
                    TenantId = tenantId,
                    MaxRetries = maxRetries,
                    Cron = cron,
                    NextOccurrence = nextOccurrence
                };
            }

            _logger.LogInformation(
                "Registered cron schedule '{Name}': {Cron} -> {Queue} (next: {Next})",
                name, cronExpression, queueName, nextOccurrence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register cron schedule '{Name}' with expression '{Cron}'",
                name, cronExpression);
            throw;
        }
    }

    public void UnregisterSchedule(string name)
    {
        lock (_lock)
        {
            if (_schedules.Remove(name))
            {
                _logger.LogInformation("Unregistered cron schedule: {Name}", name);
            }
        }
    }

    public IEnumerable<CronScheduleEntry> GetSchedules()
    {
        lock (_lock)
        {
            return _schedules.Values.ToList();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cron Scheduler Service started with {Count} schedules", _schedules.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndTriggerSchedules(stoppingToken);
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in cron scheduler");
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("Cron Scheduler Service stopped");
    }

    private async Task CheckAndTriggerSchedules(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        List<CronScheduleEntry> toTrigger = new();

        lock (_lock)
        {
            foreach (var schedule in _schedules.Values)
            {
                var next = schedule.NextOccurrence;
                if (next.HasValue && next.GetValueOrDefault() <= now)
                {
                    toTrigger.Add(schedule);
                    schedule.NextOccurrence = schedule.Cron!.GetNextOccurrence(now, TimeZoneInfo.Utc);
                }
            }
        }

        foreach (var schedule in toTrigger)
        {
            try
            {
                _logger.LogInformation("Triggering scheduled job '{Name}'", schedule.Name);
                await TriggerScheduledJob(schedule, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger scheduled job '{Name}'", schedule.Name);
            }
        }
    }

    protected virtual Task TriggerScheduledJob(CronScheduleEntry schedule, CancellationToken ct)
    {
        _logger.LogDebug(
            "Would enqueue scheduled job '{Name}' to queue '{Queue}'",
            schedule.Name, schedule.QueueName);
        return Task.CompletedTask;
    }
}

public class CronScheduleEntry
{
    public string Name { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string QueueName { get; set; } = "default";
    public string Payload { get; set; } = "{}";
    public string Namespace { get; set; } = "default";
    public string? TenantId { get; set; }
    public int MaxRetries { get; set; } = 3;
    public CronExpression? Cron { get; set; }
    public DateTime? NextOccurrence { get; set; }
}
