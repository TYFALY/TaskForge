using TaskForge.Core.Workflows;

namespace TaskForge.Api.Services;

public interface IWorkflowStore
{
    List<WorkflowDefinition> GetAll(string? ns = null);
    WorkflowDefinition? Get(Guid id);
    void Save(WorkflowDefinition workflow);
    bool Delete(Guid id);
    List<WorkflowExecution> GetExecutions(Guid workflowId);
    WorkflowExecution? GetExecution(Guid executionId);
    void SaveExecution(WorkflowExecution execution);
}
