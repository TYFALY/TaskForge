using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TaskForge.Api.Services;
using TaskForge.Core.Workflows;

namespace TaskForge.Api.Endpoints;

public static class WorkflowsEndpoint
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/workflows");
        
        group.MapGet("/", GetWorkflows);
        group.MapGet("/{id:guid}", GetWorkflow);
        group.MapGet("/{id:guid}/dag", GetWorkflowDag);
        group.MapGet("/{id:guid}/executions", GetWorkflowExecutions);
        group.MapGet("/executions/{executionId:guid}", GetExecution);
        group.MapPost("/", CreateWorkflow);
        group.MapPost("/{id:guid}/execute", ExecuteWorkflow);
        group.MapPut("/{id:guid}", UpdateWorkflow);
        group.MapDelete("/{id:guid}", DeleteWorkflow);
    }

    private static IResult GetWorkflows(
        [FromServices] IWorkflowStore store,
        [FromQuery] string? namespace_filter = "default",
        [FromQuery] string? status = null)
    {
        var workflows = store.GetAll(namespace_filter);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<WorkflowStatus>(status, true, out var s))
            workflows = workflows.Where(w => w.Status == s).ToList();
        return Results.Ok(workflows);
    }

    private static IResult GetWorkflow([FromRoute] Guid id, [FromServices] IWorkflowStore store)
    {
        var workflow = store.Get(id);
        return workflow == null ? Results.NotFound() : Results.Ok(workflow);
    }

    private static IResult GetWorkflowDag([FromRoute] Guid id, [FromServices] IWorkflowStore store, [FromServices] IWorkflowEngine engine)
    {
        var workflow = store.Get(id);
        if (workflow == null) return Results.NotFound();
        var dag = engine.ToDag(workflow);
        return Results.Ok(dag);
    }

    private static IResult GetWorkflowExecutions([FromRoute] Guid id, [FromServices] IWorkflowStore store)
    {
        var workflow = store.Get(id);
        if (workflow == null) return Results.NotFound();
        var executions = store.GetExecutions(id);
        return Results.Ok(executions);
    }

    private static IResult GetExecution([FromRoute] Guid executionId, [FromServices] IWorkflowStore store)
    {
        var execution = store.GetExecution(executionId);
        return execution == null ? Results.NotFound() : Results.Ok(execution);
    }

    private static async Task<IResult> CreateWorkflow(
        [FromBody] CreateWorkflowRequest request,
        [FromServices] IWorkflowStore store,
        [FromServices] IWorkflowEngine engine,
        ILogger<Program> logger)
    {
        var workflow = new WorkflowDefinition
        {
            Id = request.Id ?? Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description ?? "",
            Namespace = request.Namespace ?? "default",
            TenantId = request.TenantId,
            Status = WorkflowStatus.Draft,
            Steps = request.Steps?.Select((s, i) => new WorkflowStep
            {
                Id = s.Id ?? Guid.NewGuid(),
                Name = s.Name,
                Description = s.Description ?? "",
                Order = i,
                QueueName = s.QueueName ?? "default",
                Payload = s.Payload ?? "{}",
                IsParallel = s.IsParallel,
                DependsOnStepIds = s.DependsOnStepIds,
                JobType = Enum.TryParse<Core.JobType>(s.JobType, true, out var jt) ? jt : Core.JobType.Default
            }).ToList() ?? new List<WorkflowStep>()
        };

        if (request.Dag != null)
        {
            var dag = request.Dag;
            dag.WorkflowId = workflow.Id;
            workflow = engine.FromDag(dag);
            workflow.Name = request.Name;
            workflow.Namespace = request.Namespace ?? "default";
        }

        if (!engine.ValidateDag(engine.ToDag(workflow), out var error))
            return Results.BadRequest(new { error });

        store.Save(workflow);
        logger.LogInformation("Workflow {WorkflowId} created: {WorkflowName}", workflow.Id, workflow.Name);
        return Results.Created($"/api/v1/workflows/{workflow.Id}", workflow);
    }

    private static async Task<IResult> ExecuteWorkflow(
        [FromRoute] Guid id,
        [FromServices] IWorkflowStore store,
        [FromServices] IWorkflowEngine engine,
        ILogger<Program> logger)
    {
        var workflow = store.Get(id);
        if (workflow == null) return Results.NotFound();
        if (workflow.Status != WorkflowStatus.Active && workflow.Status != WorkflowStatus.Draft)
            return Results.BadRequest(new { error = "Workflow must be Active or Draft to execute" });

        var execution = await engine.ExecuteAsync(workflow);
        store.SaveExecution(execution);
        logger.LogInformation("Workflow {WorkflowId} executed: {ExecutionId}, Status: {Status}", id, execution.Id, execution.Status);
        return Results.Accepted($"/api/v1/workflows/executions/{execution.Id}", execution);
    }

    private static IResult UpdateWorkflow(
        [FromRoute] Guid id,
        [FromBody] UpdateWorkflowRequest request,
        [FromServices] IWorkflowStore store,
        [FromServices] IWorkflowEngine engine,
        ILogger<Program> logger)
    {
        var workflow = store.Get(id);
        if (workflow == null) return Results.NotFound();

        if (request.Name != null) workflow.Name = request.Name;
        if (request.Description != null) workflow.Description = request.Description;
        if (request.Status.HasValue) workflow.Status = request.Status.Value;
        if (request.Steps != null)
        {
            workflow.Steps = request.Steps.Select((s, i) => new WorkflowStep
            {
                Id = s.Id ?? Guid.NewGuid(),
                WorkflowId = id,
                Name = s.Name,
                Description = s.Description ?? "",
                Order = i,
                QueueName = s.QueueName ?? "default",
                Payload = s.Payload ?? "{}",
                IsParallel = s.IsParallel,
                DependsOnStepIds = s.DependsOnStepIds,
                JobType = Enum.TryParse<Core.JobType>(s.JobType, true, out var jt) ? jt : Core.JobType.Default
            }).ToList();
        }

        workflow.UpdatedAt = DateTime.UtcNow;
        store.Save(workflow);
        logger.LogInformation("Workflow {WorkflowId} updated", id);
        return Results.Ok(workflow);
    }

    private static IResult DeleteWorkflow([FromRoute] Guid id, [FromServices] IWorkflowStore store, ILogger<Program> logger)
    {
        if (!store.Delete(id)) return Results.NotFound();
        logger.LogInformation("Workflow {WorkflowId} deleted", id);
        return Results.NoContent();
    }
}

public class CreateWorkflowRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Namespace { get; set; }
    public string? TenantId { get; set; }
    public List<CreateStepRequest>? Steps { get; set; }
    public WorkflowDag? Dag { get; set; }
}

public class CreateStepRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? QueueName { get; set; }
    public string? Payload { get; set; }
    public string? JobType { get; set; }
    public bool IsParallel { get; set; }
    public List<Guid>? DependsOnStepIds { get; set; }
}

public class UpdateWorkflowRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public WorkflowStatus? Status { get; set; }
    public List<CreateStepRequest>? Steps { get; set; }
}
