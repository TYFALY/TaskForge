namespace TaskForge.Client.Models;

public enum JobStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
    DeadLettered,
    Scheduled,
    Cancelled
}
