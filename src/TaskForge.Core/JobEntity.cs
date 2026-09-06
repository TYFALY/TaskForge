using System.ComponentModel.DataAnnotations.Schema;

namespace TaskForge.Core;

/// <summary>
/// PostgreSQL entity representation of a job in the database.
/// </summary>
[Table("jobs")]
public class JobEntity
{
    /// <summary>
    /// Unique identifier for the job (Primary Key).
    /// </summary>
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// The name of the queue this job belongs to.
    /// </summary>
    [Column("queue_name")]
    public string QueueName { get; set; } = string.Empty;

    /// <summary>
    /// JSON serialized payload for the job.
    /// </summary>
    [Column("payload_json")]
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the job.
    /// </summary>
    [Column("status")]
    public string Status { get; set; } = "Queued";

    /// <summary>
    /// Number of retry attempts.
    /// </summary>
    [Column("retry_count")]
    public int RetryCount { get; set; }

    /// <summary>
    /// Maximum number of retries allowed.
    /// </summary>
    [Column("max_retries")]
    public int MaxRetries { get; set; }

    /// <summary>
    /// Reason for dead-lettering if applicable.
    /// </summary>
    [Column("dead_letter_reason")]
    public string? DeadLetterReason { get; set; }

    /// <summary>
    /// When the job was created.
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the job was last updated.
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Worker ID currently processing this job (null if not being processed).
    /// </summary>
    [Column("locked_by")]
    public Guid? LockedBy { get; set; }
}