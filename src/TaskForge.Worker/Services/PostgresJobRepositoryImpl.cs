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
}