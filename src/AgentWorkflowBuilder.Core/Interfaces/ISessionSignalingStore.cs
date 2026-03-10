using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Interfaces;

/// <summary>
/// Distributed session signaling store for gate/clarification responses.
/// Writes are performed by the API layer (from SignalR hub calls).
/// Workers watch for changes via change feed or polling.
/// </summary>
public interface ISessionSignalingStore
{
    /// <summary>
    /// Writes a clarification response for a waiting execution.
    /// </summary>
    Task SubmitClarificationAsync(string executionId, string answer, CancellationToken ct = default);

    /// <summary>
    /// Writes a gate response for a waiting execution.
    /// </summary>
    Task SubmitGateResponseAsync(string executionId, GateResponse response, CancellationToken ct = default);

    /// <summary>
    /// Polls for a clarification answer (used by workers when change feed is not available).
    /// Returns null if no answer has been submitted yet.
    /// </summary>
    Task<string?> TryGetClarificationAsync(string executionId, CancellationToken ct = default);

    /// <summary>
    /// Polls for a gate response (used by workers when change feed is not available).
    /// Returns null if no response has been submitted yet.
    /// </summary>
    Task<GateResponse?> TryGetGateResponseAsync(string executionId, CancellationToken ct = default);
}
