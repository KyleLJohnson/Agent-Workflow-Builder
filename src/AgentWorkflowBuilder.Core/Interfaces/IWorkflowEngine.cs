using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Interfaces;

public interface IWorkflowEngine
{
    Task<WorkflowExecutionEvent> ExecuteAsync(
        WorkflowDefinition workflow,
        string inputMessage,
        bool autoApproveGates = false,
        CancellationToken ct = default);

    IAsyncEnumerable<WorkflowExecutionEvent> ExecuteStreamingAsync(
        WorkflowDefinition workflow,
        string inputMessage,
        bool autoApproveGates = false,
        CancellationToken ct = default);
}
