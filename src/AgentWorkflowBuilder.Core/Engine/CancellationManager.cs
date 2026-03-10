using System.Collections.Concurrent;

namespace AgentWorkflowBuilder.Core.Engine;

/// <summary>
/// Tracks per-execution CancellationTokenSources for worker processes.
/// Used by ExecutionWorkerService and cancellation listeners.
/// </summary>
public sealed class CancellationManager
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokens = new();

    /// <summary>
    /// Creates and registers a linked CancellationTokenSource for an execution.
    /// </summary>
    public CancellationTokenSource CreateLinkedToken(string executionId, CancellationToken parentToken)
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        _tokens[executionId] = cts;
        return cts;
    }

    /// <summary>
    /// Cancels the execution if a token source exists.
    /// </summary>
    public void Cancel(string executionId)
    {
        if (_tokens.TryGetValue(executionId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
        }
    }

    /// <summary>
    /// Removes and disposes the token source for a completed execution.
    /// </summary>
    public void Remove(string executionId)
    {
        if (_tokens.TryRemove(executionId, out CancellationTokenSource? cts))
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// Returns true if a cancellation token source exists for the execution.
    /// </summary>
    public bool HasExecution(string executionId) => _tokens.ContainsKey(executionId);
}
