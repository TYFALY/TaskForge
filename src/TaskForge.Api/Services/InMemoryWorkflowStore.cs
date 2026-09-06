using System.Collections.Concurrent;
using TaskForge.Core.Workflows;

namespace TaskForge.Api.Services;

public class InMemoryWorkflowStore : IWorkflowStore
{
    private readonly ConcurrentDictionary<Guid, WorkflowDefinition> _workflows = new();
    private readonly ConcurrentDictionary<Guid, List<WorkflowExecution>> _executions = new();
    private readonly ConcurrentDictionary<Guid, WorkflowExecution> _executionById = new();

    public List<WorkflowDefinition> GetAll(string? ns = null)
    {
        var result = _workflows.Values.ToList();
        if (!string.IsNullOrEmpty(ns))
            result = result.Where(w => w.Namespace == ns).ToList();
        return result.OrderByDescending(w => w.CreatedAt).ToList();
    }

    public WorkflowDefinition? Get(Guid id) => _workflows.TryGetValue(id, out var w) ? w : null;

    public void Save(WorkflowDefinition workflow)
    {
        _workflows[workflow.Id] = workflow;
    }

    public bool Delete(Guid id) => _workflows.TryRemove(id, out _);

    public List<WorkflowExecution> GetExecutions(Guid workflowId)
    {
        return _executions.TryGetValue(workflowId, out var list) 
            ? list.OrderByDescending(e => e.StartedAt).ToList() 
            : new List<WorkflowExecution>();
    }

    public WorkflowExecution? GetExecution(Guid executionId)
    {
        return _executionById.TryGetValue(executionId, out var e) ? e : null;
    }

    public void SaveExecution(WorkflowExecution execution)
    {
        _executions.AddOrUpdate(
            execution.WorkflowId,
            _ => new List<WorkflowExecution> { execution },
            (_, list) => { list.Add(execution); return list; }
        );
        _executionById[execution.Id] = execution;
    }
}
