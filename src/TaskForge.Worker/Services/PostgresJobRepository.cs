using Npgsql;
using TaskForge.Core;

namespace TaskForge.Worker.Services;

/// <summary>
/// Interface for PostgreSQL job repository operations.
/// </summary>
public interface IPostgresJobRepository
{
    /// <summary>
    /// Initializes the database schema (creates tables if they don't exist).
    /// </summary>
    Task InitializeSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new job to the database.
    /// </summary>
    Task<bool> SaveJobAsync(JobEntity job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a job's status in the database.
    /// </summary>
    Task<bool> UpdateJobStatusAsync(Guid jobId, string status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a job as dead-lettered with a reason.
    /// </summary>
    Task<bool> MarkAsDeadLetteredAsync(Guid jobId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the retry count for a job.
    /// </summary>
    Task<bool> IncrementRetryCountAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets jobs stuck in PROCESSING state whose lease has expired (orphaned).
    /// Uses SELECT ... FOR UPDATE SKIP LOCKED for safe concurrent reclamation.
    /// </summary>
    Task<IReadOnlyList<JobEntity>> GetOrphanedJobsAsync(DateTime cutoff, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically resets a job back to QUEUED status for re-processing.
    /// </summary>
    Task<bool> ResetJobToQueuedAsync(Guid jobId, CancellationToken cancellationToken = default);
}