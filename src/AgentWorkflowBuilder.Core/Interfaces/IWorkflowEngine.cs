using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Interfaces;

public interface IWorkflowEngine
{
    Task<WorkflowExecutionEvent> ExecuteAsync(
        WorkflowDefinition workflow,
        string inputMessage,
        CancellationToken ct = default);

    IAsyncEnumerable<WorkflowExecutionEvent> ExecuteStreamingAsync(
        WorkflowDefinition workflow,
        string inputMessage,
        CancellationToken ct = default);
}
