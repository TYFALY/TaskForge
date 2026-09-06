namespace TaskForge.Core;

public enum JobStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
    DeadLettered,
    Scheduled
}
