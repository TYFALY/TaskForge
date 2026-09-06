using System.Text.Json.Serialization;

namespace TaskForge.Core.Workflows;

public enum WorkflowStatus
{
    Draft,
    Active,
    Running,
    Completed,
    Failed,
    Paused
}

public enum StepStatus
{
    Pending,
    Waiting,
    Running,
    Completed,
    Failed,
    Skipped
}

public enum StepTrigger
{
    Immediate,
    AfterStep,
    AfterAll,
    Cron
}

public class WorkflowDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Namespace { get; set; } = "default";
    public string? TenantId { get; set; }
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public List<WorkflowStep> Steps { get; set; } = new();
}

public class WorkflowStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Order { get; set; }
    public StepTrigger Trigger { get; set; } = StepTrigger.Immediate;
    public Guid? DependsOnStepId { get; set; }
    public List<Guid>? DependsOnStepIds { get; set; }
    public string QueueName { get; set; } = "default";
    public JobType JobType { get; set; } = JobType.Default;
    public string Payload { get; set; } = "{}";
    public WebhookPayload? WebhookPayload { get; set; }
    public string? CronExpression { get; set; }
    public int MaxRetries { get; set; } = 3;
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public bool IsParallel { get; set; }
    public string? ErrorHandlerStepId { get; set; }
}

public class WorkflowExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public string? WorkflowName { get; set; }
    public string Namespace { get; set; } = "default";
    public string? TenantId { get; set; }
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Running;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? CurrentStepId { get; set; }
    public string? ErrorMessage { get; set; }
    public List<StepExecution> StepExecutions { get; set; } = new();
}

public class StepExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExecutionId { get; set; }
    public Guid StepId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? ParentStepExecutionId { get; set; }
}

public class WorkflowDag
{
    public Guid WorkflowId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<DagNode> Nodes { get; set; } = new();
    public List<DagEdge> Edges { get; set; } = new();
}


public class DagNode
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public string QueueName { get; set; } = "default";
    public string Payload { get; set; } = "{}";
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public bool IsParallel { get; set; }
}

public class DagEdge
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public string? Label { get; set; }
}
