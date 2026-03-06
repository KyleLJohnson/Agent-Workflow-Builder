using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Interfaces;

/// <summary>
/// Persists execution records for history, recovery, and audit.
/// </summary>
public interface IExecutionStore
{
    /// <summary>
    /// Upserts an execution record (create or update).
    /// </summary>
    Task SaveAsync(ExecutionRecord record, CancellationToken ct = default);

    /// <summary>
    /// Retrieves an execution record by ID.
    /// </summary>
    Task<ExecutionRecord?> GetAsync(string executionId, CancellationToken ct = default);

    /// <summary>
    /// Lists all execution records for a specific workflow.
    /// </summary>
    Task<IReadOnlyList<ExecutionRecord>> ListByWorkflowAsync(string workflowId, CancellationToken ct = default);

    /// <summary>
    /// Returns all executions currently in a paused state (for recovery on startup).
    /// </summary>
    Task<IReadOnlyList<ExecutionRecord>> GetPausedAsync(CancellationToken ct = default);
}
