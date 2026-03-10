using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Interfaces;

/// <summary>
/// Abstracts the execution queue for dispatching and consuming workflow execution requests.
/// Implemented by Azure Service Bus in production, or in-memory for local development.
/// </summary>
public interface IExecutionQueue
{
    /// <summary>
    /// Enqueues a workflow execution request.
    /// </summary>
    Task EnqueueAsync(ExecutionMessage message, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a cancellation signal for a running execution.
    /// </summary>
    Task EnqueueCancellationAsync(string executionId, CancellationToken ct = default);
}
