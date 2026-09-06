using Microsoft.Data.Sqlite;
using TaskForge.Core;

namespace TaskForge.Api.Services.Embedded;

public interface IEmbeddedJobRepository
{
    Task InitializeSchemaAsync(CancellationToken ct = default);
    Task SaveJobAsync(JobEntity e, CancellationToken ct = default);
    Task UpdateJobAsync(JobEntity e, CancellationToken ct = default);
    Task<JobEntity?> GetJobAsync(Guid id, CancellationToken ct = default);
    Task<List<JobEntity>> GetRecentJobsAsync(int limit = 100, CancellationToken ct = default);
    Task<(long Total, long Failed, long DLQ)> GetJobStatsAsync(CancellationToken ct = default);
}

public class EmbeddedJobRepository : IEmbeddedJobRepository
{
    private readonly string _cs;
    private readonly ILogger<EmbeddedJobRepository> _log;
    private bool _init;

    public EmbeddedJobRepository(string path, ILogger<EmbeddedJobRepository> log)
    {
        _cs = $"Data Source={(Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path))}";
        _log = log;
    }

    public async Task InitializeSchemaAsync(CancellationToken ct = default)
    {
        if (_init) return;
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(ct);
        await using var cmd = new SqliteCommand(@"CREATE TABLE IF NOT EXISTS jobs (id TEXT PRIMARY KEY,queue_name TEXT NOT NULL,payload_json TEXT NOT NULL,status TEXT NOT NULL,retry_count INTEGER NOT NULL DEFAULT 0,max_retries INTEGER NOT NULL DEFAULT 3,dead_letter_reason TEXT,created_at TEXT NOT NULL,updated_at TEXT NOT NULL,locked_by TEXT);CREATE INDEX IF NOT EXISTS idx_status ON jobs(status);CREATE INDEX IF NOT EXISTS idx_created ON jobs(created_at DESC)", c);
        await cmd.ExecuteNonQueryAsync(ct);
        _init = true;
        _log.LogInformation("DB schema ready");
    }

    public async Task SaveJobAsync(JobEntity e, CancellationToken ct = default)
    {
        await InitializeSchemaAsync(ct);
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(ct);
        var cmd = new SqliteCommand(@"INSERT INTO jobs (id,queue_name,payload_json,status,retry_count,max_retries,created_at,updated_at) VALUES (@i,@q,@p,@s,@r,@m,@c,@u)", c);
        cmd.Parameters.AddWithValue("@i", e.Id.ToString());
        cmd.Parameters.AddWithValue("@q", e.QueueName);
        cmd.Parameters.AddWithValue("@p", e.PayloadJson);
        cmd.Parameters.AddWithValue("@s", e.Status);
        cmd.Parameters.AddWithValue("@r", e.RetryCount);
        cmd.Parameters.AddWithValue("@m", e.MaxRetries);
        cmd.Parameters.AddWithValue("@c", e.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@u", e.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateJobAsync(JobEntity e, CancellationToken ct = default)
    {
        await InitializeSchemaAsync(ct);
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(ct);
        var cmd = new SqliteCommand(@"UPDATE jobs SET status=@s,retry_count=@r,updated_at=@u,dead_letter_reason=@d WHERE id=@i", c);
        cmd.Parameters.AddWithValue("@i", e.Id.ToString());
        cmd.Parameters.AddWithValue("@s", e.Status);
        cmd.Parameters.AddWithValue("@r", e.RetryCount);
        cmd.Parameters.AddWithValue("@u", e.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@d", e.DeadLetterReason ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<JobEntity?> GetJobAsync(Guid id, CancellationToken ct = default)
    {
        await InitializeSchemaAsync(ct);
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(ct);
        var cmd = new SqliteCommand("SELECT id,queue_name,payload_json,status,retry_count,max_retries,dead_letter_reason,created_at,updated_at,locked_by FROM jobs WHERE id=@i", c);
        cmd.Parameters.AddWithValue("@i", id.ToString());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<List<JobEntity>> GetRecentJobsAsync(int limit = 100, CancellationToken ct = default)
    {
        await InitializeSchemaAsync(ct);
        var l = new List<JobEntity>();
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(ct);
        var cmd = new SqliteCommand($"SELECT id,queue_name,payload_json,status,retry_count,max_retries,dead_letter_reason,created_at,updated_at,locked_by FROM jobs ORDER BY created_at DESC LIMIT {limit}", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) l.Add(Map(r));
        return l;
    }

    public async Task<(long Total, long Failed, long DLQ)> GetJobStatsAsync(CancellationToken ct = default)
    {
        await InitializeSchemaAsync(ct);
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(ct);
        var cmd = new SqliteCommand(@"SELECT COUNT(CASE WHEN status IN('Completed','Processing') THEN 1 END),COUNT(CASE WHEN status='Failed' THEN 1 END),COUNT(CASE WHEN status='DeadLettered' THEN 1 END) FROM jobs", c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2)) : (0L, 0L, 0L);
    }

    private static JobEntity Map(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(0)), QueueName = r.GetString(1), PayloadJson = r.GetString(2),
        Status = r.GetString(3), RetryCount = r.GetInt32(4), MaxRetries = r.GetInt32(5),
        DeadLetterReason = r.IsDBNull(6) ? null : r.GetString(6),
        CreatedAt = DateTime.Parse(r.GetString(7)), UpdatedAt = DateTime.Parse(r.GetString(8)),
        LockedBy = r.IsDBNull(9) ? null : Guid.Parse(r.GetString(9))
    };
}