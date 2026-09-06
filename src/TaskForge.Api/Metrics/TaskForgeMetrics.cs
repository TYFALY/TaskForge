using Prometheus;

namespace TaskForge.Api.Metrics;

/// <summary>
/// Centralized Prometheus metrics for TaskForge.
/// </summary>
public static class TaskForgeMetrics
{
    public static readonly Counter JobsEnqueuedTotal = Prometheus.Metrics.CreateCounter(
        "taskforge_jobs_enqueued_total",
        "Total number of jobs enqueued",
        new CounterConfiguration
        {
            LabelNames = new[] { "queue", "namespace", "tenant_id", "job_type" }
        });

    public static readonly Counter JobsProcessedTotal = Prometheus.Metrics.CreateCounter(
        "taskforge_jobs_processed_total",
        "Total number of jobs processed",
        new CounterConfiguration
        {
            LabelNames = new[] { "queue", "namespace", "tenant_id", "status" }
        });

    public static readonly Histogram JobExecutionDuration = Prometheus.Metrics.CreateHistogram(
        "taskforge_job_execution_duration_seconds",
        "Job execution duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "queue", "namespace", "job_type" },
            Buckets = new[] { 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0 }
        });

    public static readonly Gauge QueueDepth = Prometheus.Metrics.CreateGauge(
        "taskforge_queue_depth",
        "Current number of jobs in queue",
        new GaugeConfiguration
        {
            LabelNames = new[] { "queue", "namespace" }
        });

    public static readonly Gauge ActiveWorkers = Prometheus.Metrics.CreateGauge(
        "taskforge_active_workers",
        "Number of active workers",
        new GaugeConfiguration
        {
            LabelNames = new[] { "namespace" }
        });

    public static readonly Counter WebhooksTotal = Prometheus.Metrics.CreateCounter(
        "taskforge_webhooks_total",
        "Total number of webhook executions",
        new CounterConfiguration
        {
            LabelNames = new[] { "queue", "namespace", "status_code" }
        });

    public static readonly Histogram WebhookDuration = Prometheus.Metrics.CreateHistogram(
        "taskforge_webhook_duration_seconds",
        "Webhook request duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "queue", "namespace" },
            Buckets = new[] { 0.01, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0, 30.0 }
        });

    public static readonly Counter ScheduledJobsTriggered = Prometheus.Metrics.CreateCounter(
        "taskforge_scheduled_jobs_triggered_total",
        "Total number of scheduled jobs triggered",
        new CounterConfiguration
        {
            LabelNames = new[] { "queue", "namespace", "cron_expression" }
        });

    public static readonly Counter RateLimitedRequests = Prometheus.Metrics.CreateCounter(
        "taskforge_rate_limited_requests_total",
        "Total number of rate-limited requests",
        new CounterConfiguration
        {
            LabelNames = new[] { "tenant_id" }
        });

    /// <summary>
    /// Records a job enqueue event.
    /// </summary>
    public static void RecordJobEnqueued(string queue, string @namespace, string? tenantId, string jobType)
    {
        JobsEnqueuedTotal.WithLabels(queue, @namespace, tenantId ?? "none", jobType).Inc();
    }

    /// <summary>
    /// Records a job processing completion.
    /// </summary>
    public static void RecordJobProcessed(string queue, string @namespace, string? tenantId, string status)
    {
        JobsProcessedTotal.WithLabels(queue, @namespace, tenantId ?? "none", status).Inc();
    }

    /// <summary>
    /// Records job execution duration.
    /// </summary>
    public static void RecordJobDuration(string queue, string @namespace, string jobType, double seconds)
    {
        JobExecutionDuration.WithLabels(queue, @namespace, jobType).Observe(seconds);
    }

    /// <summary>
    /// Updates queue depth gauge.
    /// </summary>
    public static void UpdateQueueDepth(string queue, string @namespace, long depth)
    {
        QueueDepth.WithLabels(queue, @namespace).Set(depth);
    }
}
