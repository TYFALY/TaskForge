using TaskForge.Client.Models;

namespace TaskForge.Client;

/// <summary>
/// Strongly-typed client for interacting with the TaskForge distributed job engine.
/// </summary>
public interface ITaskForgeClient
{
    /// <summary>
    /// Enqueue an asynchronous job with a string or JSON payload.
    /// </summary>
    Task<JobResponse> EnqueueAsync(
        string queueName,
        string payload,
        int maxRetries = 3,
        string? tenantId = null,
        string? jobNamespace = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a typed object as a JSON payload.
    /// </summary>
    Task<JobResponse> EnqueueAsync<T>(
        string queueName,
        T payload,
        int maxRetries = 3,
        string? tenantId = null,
        string? jobNamespace = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue an outbound webhook task with SSRF-safe execution.
    /// </summary>
    Task<JobResponse> EnqueueWebhookAsync(
        string targetUrl,
        string method = "POST",
        string body = "",
        Dictionary<string, string>? headers = null,
        string queueName = "webhooks",
        int maxRetries = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch the current execution status and metadata of a job.
    /// </summary>
    Task<JobResponse?> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retry / replay a failed or dead-lettered job.
    /// </summary>
    Task<bool> RetryJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cooperatively cancel an in-flight or queued job.
    /// </summary>
    Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk replay all Dead-Letter Queue (DLQ) jobs.
    /// </summary>
    Task<int> ReplayAllDlqAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Block until a job reaches a terminal state (Completed, Failed, DeadLettered, or Cancelled).
    /// </summary>
    Task<JobResponse> WaitForCompletionAsync(
        Guid jobId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
