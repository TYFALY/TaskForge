using System.Collections.Concurrent;
using TaskForge.Core;

namespace TaskForge.Core.Workflows;

public interface IWorkflowEngine
{
    Task<WorkflowExecution> ExecuteAsync(WorkflowDefinition workflow, CancellationToken ct = default);
    WorkflowDag ToDag(WorkflowDefinition workflow);
    WorkflowDefinition FromDag(WorkflowDag dag);
    bool ValidateDag(WorkflowDag dag, out string? error);
    List<DagNode> GetExecutableSteps(WorkflowDag dag, List<StepExecution> completed);
}

public class WorkflowEngine : IWorkflowEngine
{
    private readonly ConcurrentDictionary<Guid, WorkflowExecution> _activeExecutions = new();

    public async Task<WorkflowExecution> ExecuteAsync(WorkflowDefinition workflow, CancellationToken ct = default)
    {
        var execution = new WorkflowExecution
        {
            WorkflowId = workflow.Id,
            WorkflowName = workflow.Name,
            Namespace = workflow.Namespace,
            TenantId = workflow.TenantId,
            Status = WorkflowStatus.Running,
            StartedAt = DateTime.UtcNow
        };

        _activeExecutions[execution.Id] = execution;
        var dag = ToDag(workflow);
        var completedSteps = new List<StepExecution>();

        try
        {
            while (true)
            {
                var executableSteps = GetExecutableSteps(dag, completedSteps);
                if (executableSteps.Count == 0) break;

                var parallelSteps = executableSteps.Where(s => s.IsParallel).ToList();
                var sequentialSteps = executableSteps.Where(s => !s.IsParallel).ToList();

                foreach (var step in sequentialSteps)
                {
                    if (ct.IsCancellationRequested) break;
                    var stepExec = await ExecuteStepAsync(execution, step, ct);
                    completedSteps.Add(stepExec);
                    if (stepExec.Status == StepStatus.Failed) throw new Exception($"Step {step.Name} failed");
                }

                if (parallelSteps.Count > 0)
                {
                    var tasks = parallelSteps.Select(s => ExecuteStepAsync(execution, s, ct));
                    var results = await Task.WhenAll(tasks);
                    completedSteps.AddRange(results);
                    if (results.Any(r => r.Status == StepStatus.Failed)) throw new Exception("One or more parallel steps failed");
                }
            }

            execution.Status = WorkflowStatus.Completed;
            execution.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            execution.Status = WorkflowStatus.Failed;
            execution.ErrorMessage = ex.Message;
            execution.CompletedAt = DateTime.UtcNow;
        }

        execution.StepExecutions = completedSteps;
        return execution;
    }

    private async Task<StepExecution> ExecuteStepAsync(WorkflowExecution execution, DagNode step, CancellationToken ct)
    {
        var stepExec = new StepExecution
        {
            ExecutionId = execution.Id,
            StepId = step.Id,
            StepName = step.Name,
            Status = StepStatus.Running,
            StartedAt = DateTime.UtcNow
        };

        execution.CurrentStepId = step.Id.ToString();

        try
        {
            await Task.Delay(100, ct);
            stepExec.Status = StepStatus.Completed;
            stepExec.Result = $"Step {step.Name} completed successfully";
        }
        catch (Exception ex)
        {
            stepExec.Status = StepStatus.Failed;
            stepExec.ErrorMessage = ex.Message;
        }

        stepExec.CompletedAt = DateTime.UtcNow;
        return stepExec;
    }

    public WorkflowDag ToDag(WorkflowDefinition workflow)
    {
        var dag = new WorkflowDag
        {
            WorkflowId = workflow.Id,
            Name = workflow.Name
        };

        var stepDict = workflow.Steps.ToDictionary(s => s.Id);
        var positionMap = CalculateNodePositions(workflow.Steps);

        foreach (var step in workflow.Steps)
        {
            dag.Nodes.Add(new DagNode
            {
                Id = step.Id,
                Name = step.Name,
                Description = step.Description,
                X = positionMap[step.Id].X,
                Y = positionMap[step.Id].Y,
                QueueName = step.QueueName,
                Payload = step.Payload,
                Status = step.Status,
                IsParallel = step.IsParallel
            });
        }

        foreach (var step in workflow.Steps)
        {
            if (step.DependsOnStepId.HasValue)
            {
                dag.Edges.Add(new DagEdge
                {
                    Id = Guid.NewGuid(),
                    SourceId = step.DependsOnStepId.Value,
                    TargetId = step.Id
                });
            }

            if (step.DependsOnStepIds != null)
            {
                foreach (var depId in step.DependsOnStepIds)
                {
                    dag.Edges.Add(new DagEdge
                    {
                        Id = Guid.NewGuid(),
                        SourceId = depId,
                        TargetId = step.Id
                    });
                }
            }
        }

        return dag;
    }

    private Dictionary<Guid, (int X, int Y)> CalculateNodePositions(List<WorkflowStep> steps)
    {
        var positions = new Dictionary<Guid, (int X, int Y)>();
        var levels = TopologicalSort(steps);
        var levelHeight = 120;
        var nodeWidth = 200;

        for (int level = 0; level < levels.Count; level++)
        {
            var nodesInLevel = levels[level];
            var totalWidth = nodesInLevel.Count * nodeWidth;
            var startX = -totalWidth / 2;

            for (int i = 0; i < nodesInLevel.Count; i++)
            {
                positions[nodesInLevel[i].Id] = (startX + i * nodeWidth + nodeWidth / 2, level * levelHeight);
            }
        }

        return positions;
    }

    private List<List<WorkflowStep>> TopologicalSort(List<WorkflowStep> steps)
    {
        var result = new List<List<WorkflowStep>>();
        var remaining = new HashSet<Guid>(steps.Select(s => s.Id));
        var deps = steps.Where(s => s.DependsOnStepId.HasValue).ToDictionary(s => s.Id, s => s.DependsOnStepId!.Value);
        var multiDeps = steps.Where(s => s.DependsOnStepIds != null).ToDictionary(s => s.Id, s => s.DependsOnStepIds!);

        while (remaining.Count > 0)
        {
            var level = new List<WorkflowStep>();
            var removable = new List<Guid>();

            foreach (var step in steps.Where(s => remaining.Contains(s.Id)))
            {
                if (!deps.TryGetValue(step.Id, out var dep) && !multiDeps.TryGetValue(step.Id, out var mDep))
                {
                    level.Add(step);
                    removable.Add(step.Id);
                }
                else if (deps.ContainsKey(step.Id) && !remaining.Contains(dep))
                {
                    level.Add(step);
                    removable.Add(step.Id);
                }
                else if (multiDeps.ContainsKey(step.Id) && multiDeps[step.Id].All(d => !remaining.Contains(d)))
                {
                    level.Add(step);
                    removable.Add(step.Id);
                }
            }

            foreach (var id in removable) remaining.Remove(id);

            if (level.Count == 0 && remaining.Count > 0)
            {
                foreach (var id in remaining)
                {
                    var step = steps.First(s => s.Id == id);
                    level.Add(step);
                }
                break;
            }

            result.Add(level);
        }

        return result;
    }

    public WorkflowDefinition FromDag(WorkflowDag dag)
    {
        return new WorkflowDefinition
        {
            Id = dag.WorkflowId,
            Name = dag.Name,
            Steps = dag.Nodes.Select((n, i) => new WorkflowStep
            {
                Id = n.Id,
                Name = n.Name,
                Description = n.Description,
                Order = i,
                QueueName = n.QueueName,
                Payload = n.Payload,
                Status = n.Status,
                IsParallel = n.IsParallel,
                DependsOnStepIds = dag.Edges.Where(e => e.TargetId == n.Id).Select(e => e.SourceId).ToList()
            }).ToList()
        };
    }

    public bool ValidateDag(WorkflowDag dag, out string? error)
    {
        error = null;

        var nodeIds = dag.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var edge in dag.Edges)
        {
            if (!nodeIds.Contains(edge.SourceId))
            {
                error = $"Edge references non-existent source node: {edge.SourceId}";
                return false;
            }
            if (!nodeIds.Contains(edge.TargetId))
            {
                error = $"Edge references non-existent target node: {edge.TargetId}";
                return false;
            }
        }

        if (HasCycle(dag))
        {
            error = "Workflow contains a cycle, which is not allowed in a DAG";
            return false;
        }

        return true;
    }

    private bool HasCycle(WorkflowDag dag)
    {
        var visited = new HashSet<Guid>();
        var recStack = new HashSet<Guid>();

        bool DFS(Guid nodeId)
        {
            visited.Add(nodeId);
            recStack.Add(nodeId);

            foreach (var edge in dag.Edges.Where(e => e.SourceId == nodeId))
            {
                if (!visited.Contains(edge.TargetId) && DFS(edge.TargetId)) return true;
                else if (recStack.Contains(edge.TargetId)) return true;
            }

            recStack.Remove(nodeId);
            return false;
        }

        foreach (var node in dag.Nodes)
        {
            if (!visited.Contains(node.Id) && DFS(node.Id)) return true;
        }

        return false;
    }

    public List<DagNode> GetExecutableSteps(WorkflowDag dag, List<StepExecution> completed)
    {
        var completedIds = completed.Where(s => s.Status == StepStatus.Completed).Select(s => s.StepId).ToHashSet();
        var failedIds = completed.Where(s => s.Status == StepStatus.Failed).Select(s => s.StepId).ToHashSet();

        return dag.Nodes.Where(node =>
        {
            if (completedIds.Contains(node.Id) || failedIds.Contains(node.Id)) return false;

            var incomingEdges = dag.Edges.Where(e => e.TargetId == node.Id).ToList();
            if (incomingEdges.Count == 0) return true;

            return incomingEdges.All(e => completedIds.Contains(e.SourceId));
        }).ToList();
    }
}
