using System.Collections.Concurrent;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Microsoft.Extensions.Configuration;

namespace AgentWorkflowBuilder.Core.Engine;

/// <summary>
/// Manages active workflow execution sessions, supporting pause/resume for clarification, gates, and send-back.
/// </summary>
public class ExecutionSessionManager
{
    private readonly ConcurrentDictionary<string, ExecutionSession> _sessions = new();
    private readonly IExecutionStore _executionStore;
    private readonly int _clarificationTimeoutMinutes;

    public ExecutionSessionManager(IConfiguration configuration, IExecutionStore executionStore)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(executionStore);
        _executionStore = executionStore;
        _clarificationTimeoutMinutes = int.TryParse(
            configuration["Workflow:ClarificationTimeoutMinutes"], out int val) ? val : 10;
    }

    /// <summary>
    /// Creates a new execution session.
    /// </summary>
    public ExecutionSession CreateSession(string executionId)
    {
        ExecutionSession session = new(executionId);
        if (!_sessions.TryAdd(executionId, session))
            throw new InvalidOperationException($"Execution session '{executionId}' already exists.");
        return session;
    }

    /// <summary>
    /// Blocks until a clarification answer is submitted, or times out.
    /// Checkpoints the paused state to the execution store.
    /// </summary>
    public async Task<string> WaitForClarificationAsync(string executionId, CancellationToken ct)
    {
        ExecutionSession session = GetSession(executionId);
        session.ClarificationTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await SaveCheckpointAsync(executionId, ExecutionStatus.Paused, PauseType.Clarification, ct);

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromMinutes(_clarificationTimeoutMinutes));

        await using CancellationTokenRegistration _ = linked.Token.Register(
            () => session.ClarificationTcs.TrySetException(
                new TimeoutException($"Clarification timeout after {_clarificationTimeoutMinutes} minutes.")));

        string result = await session.ClarificationTcs.Task;

        await SaveCheckpointAsync(executionId, ExecutionStatus.Running, PauseType.None, ct);
        return result;
    }

    /// <summary>
    /// Submits a clarification answer to unblock the waiting engine.
    /// </summary>
    public void SubmitClarification(string executionId, string answer)
    {
        ExecutionSession session = GetSession(executionId);
        session.ClarificationTcs?.TrySetResult(answer);
    }

    /// <summary>
    /// Blocks until a gate response is submitted, or times out.
    /// Checkpoints the paused state to the execution store.
    /// </summary>
    public async Task<GateResponse> WaitForGateResponseAsync(string executionId, CancellationToken ct)
    {
        ExecutionSession session = GetSession(executionId);
        session.GateTcs = new TaskCompletionSource<GateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        await SaveCheckpointAsync(executionId, ExecutionStatus.Paused, PauseType.Gate, ct);

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromMinutes(_clarificationTimeoutMinutes));

        await using CancellationTokenRegistration _ = linked.Token.Register(
            () => session.GateTcs.TrySetException(
                new TimeoutException($"Gate approval timeout after {_clarificationTimeoutMinutes} minutes.")));

        GateResponse result = await session.GateTcs.Task;

        PauseType resumePause = result.Status == GateResponseStatus.SendBack ? PauseType.SendBack : PauseType.None;
        await SaveCheckpointAsync(executionId, ExecutionStatus.Running, resumePause, ct);
        return result;
    }

    /// <summary>
    /// Submits a gate response (approve/reject/send-back) to unblock the waiting engine.
    /// </summary>
    public void SubmitGateResponse(string executionId, GateResponse response)
    {
        ExecutionSession session = GetSession(executionId);
        session.GateTcs?.TrySetResult(response);
    }

    /// <summary>
    /// Removes the session on completion.
    /// </summary>
    public void RemoveSession(string executionId)
    {
        _sessions.TryRemove(executionId, out _);
    }

    /// <summary>
    /// Returns true if a session exists for the given execution ID.
    /// </summary>
    public bool HasSession(string executionId) => _sessions.ContainsKey(executionId);

    /// <summary>
    /// Attempts to get the session for the given execution ID.
    /// </summary>
    public bool TryGetSession(string executionId, out ExecutionSession? session)
        => _sessions.TryGetValue(executionId, out session);

    /// <summary>
    /// Saves a checkpoint of the current execution state to the durable store.
    /// </summary>
    private async Task SaveCheckpointAsync(string executionId, ExecutionStatus status, PauseType pauseType, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(executionId, out ExecutionSession? session))
            return;

        ExecutionRecord record = new()
        {
            Id = executionId,
            WorkflowId = session.WorkflowId ?? string.Empty,
            Status = status,
            PauseType = pauseType,
            CurrentNodeId = session.CurrentNodeId,
            AgentOutputs = new Dictionary<string, string>(session.AgentOutputs),
            AccumulatedContext = session.AccumulatedContext
        };

        await _executionStore.SaveAsync(record, ct);
    }

    private ExecutionSession GetSession(string executionId)
    {
        if (!_sessions.TryGetValue(executionId, out ExecutionSession? session))
            throw new InvalidOperationException($"Execution session '{executionId}' not found.");
        return session;
    }
}

/// <summary>
/// Holds state for a single in-flight workflow execution.
/// </summary>
public class ExecutionSession
{
    public ExecutionSession(string executionId)
    {
        ExecutionId = executionId;
    }

    public string ExecutionId { get; }
    public string? WorkflowId { get; set; }
    public string? CurrentNodeId { get; set; }
    public string? AccumulatedContext { get; set; }
    public Dictionary<string, string> AgentOutputs { get; } = new();
    public TaskCompletionSource<string>? ClarificationTcs { get; set; }
    public TaskCompletionSource<GateResponse>? GateTcs { get; set; }
}
