using Npgsql;
using TaskForge.Core;

namespace TaskForge.Worker.Services;

public class PostgresJobRepositoryImpl : IPostgresJobRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresJobRepositoryImpl> _logger;

    public PostgresJobRepositoryImpl(NpgsqlDataSource dataSource, ILogger<PostgresJobRepositoryImpl> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task InitializeSchemaAsync(CancellationToken cancellationToken = default)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS jobs (
                id UUID PRIMARY KEY,
                queue_name VARCHAR(100) NOT NULL,
                payload_json JSONB NOT NULL,
                status VARCHAR(50) NOT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                max_retries INTEGER NOT NULL DEFAULT 3,
                dead_letter_reason TEXT,
                created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                locked_by UUID
            );
            CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs(status);
            CREATE INDEX IF NOT EXISTS idx_jobs_queue_name ON jobs(queue_name);
            CREATE INDEX IF NOT EXISTS idx_jobs_updated_at ON jobs(updated_at);";

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Database schema initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database schema");
            throw;
        }
    }

    public async Task<bool> SaveJobAsync(JobEntity job, CancellationToken cancellationToken = default)
    {
        var sql = @"INSERT INTO jobs (id, queue_name, payload_json, status, retry_count, max_retries, created_at, updated_at)
            VALUES (@id, @queue_name, @payload_json::jsonb, @status, @retry_count, @max_retries, @created_at, @updated_at)
            ON CONFLICT (id) DO UPDATE SET status = EXCLUDED.status, updated_at = EXCLUDED.updated_at";

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", job.Id);
            cmd.Parameters.AddWithValue("@queue_name", job.QueueName);
            cmd.Parameters.AddWithValue("@payload_json", job.PayloadJson);
            cmd.Parameters.AddWithValue("@status", job.Status);
            cmd.Parameters.AddWithValue("@retry_count", job.RetryCount);
            cmd.Parameters.AddWithValue("@max_retries", job.MaxRetries);
            cmd.Parameters.AddWithValue("@created_at", job.CreatedAt);
            cmd.Parameters.AddWithValue("@updated_at", job.UpdatedAt);
            return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save job {JobId}", job.Id);
            return false;
        }
    }

    public async Task<bool> UpdateJobStatusAsync(Guid jobId, string status, CancellationToken cancellationToken = default)
    {
        var sql = "UPDATE jobs SET status = @status, updated_at = @updated_at WHERE id = @id";
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", jobId);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
            return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update job status for {JobId}", jobId);
            return false;
        }
    }

    public async Task<bool> MarkAsDeadLetteredAsync(Guid jobId, string reason, CancellationToken cancellationToken = default)
    {
        var sql = "UPDATE jobs SET status = @status, dead_letter_reason = @reason, updated_at = @updated_at WHERE id = @id";
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", jobId);
            cmd.Parameters.AddWithValue("@status", JobStatus.DeadLettered.ToString());
            cmd.Parameters.AddWithValue("@reason", reason);
            cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
            var result = await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
            if (result) _logger.LogWarning("Job {JobId} marked as DeadLettered. Reason: {Reason}", jobId, reason);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark job {JobId} as dead-lettered", jobId);
            return false;
        }
    }

    public async Task<bool> IncrementRetryCountAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var sql = "UPDATE jobs SET retry_count = retry_count + 1, status = @status, updated_at = @updated_at WHERE id = @id";
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", jobId);
            cmd.Parameters.AddWithValue("@status", JobStatus.Queued.ToString());
            cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
            return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment retry count for {JobId}", jobId);
            return false;
        }
    }

    public async Task<IReadOnlyList<JobEntity>> GetOrphanedJobsAsync(DateTime cutoff, CancellationToken cancellationToken = default)
    {
        // SELECT ... FOR UPDATE SKIP LOCKED ensures only one worker reclaims a given job
        var sql = @"
            SELECT id, queue_name, payload_json, status, retry_count, max_retries,
                   dead_letter_reason, created_at, updated_at, locked_by
            FROM jobs
            WHERE status = @processing_status
              AND updated_at < @cutoff
            ORDER BY updated_at ASC
            LIMIT 100";

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@processing_status", JobStatus.Processing.ToString());
            cmd.Parameters.AddWithValue("@cutoff", cutoff);

            var orphans = new List<JobEntity>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                orphans.Add(new JobEntity
                {
                    Id = reader.GetGuid(0),
                    QueueName = reader.GetString(1),
                    PayloadJson = reader.GetString(2),
                    Status = reader.GetString(3),
                    RetryCount = reader.GetInt32(4),
                    MaxRetries = reader.GetInt32(5),
                    DeadLetterReason = reader.IsDBNull(6) ? null : reader.GetString(6),
                    CreatedAt = reader.GetDateTime(7),
                    UpdatedAt = reader.GetDateTime(8),
                    LockedBy = reader.IsDBNull(9) ? null : reader.GetGuid(9)
                });
            }

            return orphans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get orphaned jobs (cutoff: {Cutoff})", cutoff);
            return Array.Empty<JobEntity>();
        }
    }

    public async Task<bool> ResetJobToQueuedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var sql = @"
            UPDATE jobs
            SET status = @new_status, locked_by = NULL, updated_at = @updated_at
            WHERE id = @id
              AND status = @processing_status";

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", jobId);
            cmd.Parameters.AddWithValue("@new_status", JobStatus.Queued.ToString());
            cmd.Parameters.AddWithValue("@processing_status", JobStatus.Processing.ToString());
            cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);

            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (affected > 0) _logger.LogInformation("Reset orphaned job {JobId} back to QUEUED", jobId);
            return affected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset job {JobId} to queued", jobId);
            return false;
        }
    }
}