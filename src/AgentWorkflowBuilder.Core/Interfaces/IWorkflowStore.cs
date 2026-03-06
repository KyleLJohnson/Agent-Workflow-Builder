using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Interfaces;

public interface IWorkflowStore
{
    Task<IReadOnlyList<WorkflowDefinition>> ListAsync(string? userId = null, CancellationToken ct = default);
    Task<WorkflowDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task<WorkflowDefinition> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default);
    Task<WorkflowDefinition> UpdateAsync(WorkflowDefinition definition, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
