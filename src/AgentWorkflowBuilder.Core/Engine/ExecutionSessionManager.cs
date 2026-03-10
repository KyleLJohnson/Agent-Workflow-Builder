using System.Collections.Concurrent;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentWorkflowBuilder.Core.Engine;

/// <summary>
/// Manages active workflow execution sessions, supporting pause/resume for clarification, gates, and send-back.
/// When <see cref="ISessionSignalingStore"/> is configured, uses distributed Cosmos DB signaling with polling.
/// Otherwise, falls back to in-memory TaskCompletionSource signaling (single-instance mode).
/// </summary>
public class ExecutionSessionManager
{
    private readonly ConcurrentDictionary<string, ExecutionSession> _sessions = new();
    private readonly IExecutionStore _executionStore;
    private readonly ISessionSignalingStore? _signalingStore;
    private readonly ILogger<ExecutionSessionManager> _logger;
    private readonly int _clarificationTimeoutMinutes;
    private readonly int _pollingIntervalMs;

    public ExecutionSessionManager(
        IConfiguration configuration,
        IExecutionStore executionStore,
        ILogger<ExecutionSessionManager> logger,
        ISessionSignalingStore? signalingStore = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(executionStore);
        ArgumentNullException.ThrowIfNull(logger);
        _executionStore = executionStore;
        _signalingStore = signalingStore;
        _logger = logger;
        _clarificationTimeoutMinutes = int.TryParse(
            configuration["Workflow:ClarificationTimeoutMinutes"], out int val) ? val : 10;
        _pollingIntervalMs = int.TryParse(
            configuration["Workflow:SignalingPollingIntervalMs"], out int pollVal) ? pollVal : 2000;
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
    /// In distributed mode, polls the signaling store. In local mode, uses in-memory TCS.
    /// </summary>
    public async Task<string> WaitForClarificationAsync(string executionId, CancellationToken ct)
    {
        await SaveCheckpointAsync(executionId, ExecutionStatus.Paused, PauseType.Clarification, ct);

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromMinutes(_clarificationTimeoutMinutes));

        if (_signalingStore is not null)
        {
            // Distributed mode: poll Cosmos for a clarification document
            string result = await PollForClarificationAsync(executionId, linked.Token);
            await SaveCheckpointAsync(executionId, ExecutionStatus.Running, PauseType.None, ct);
            return result;
        }

        // Local mode: in-memory TCS
        ExecutionSession session = GetSession(executionId);
        session.ClarificationTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using CancellationTokenRegistration _ = linked.Token.Register(
            () => session.ClarificationTcs.TrySetException(
                new TimeoutException($"Clarification timeout after {_clarificationTimeoutMinutes} minutes.")));

        string answer = await session.ClarificationTcs.Task;
        await SaveCheckpointAsync(executionId, ExecutionStatus.Running, PauseType.None, ct);
        return answer;
    }

    /// <summary>
    /// Submits a clarification answer. In distributed mode, writes to the signaling store.
    /// In local mode, completes the in-memory TCS.
    /// </summary>
    public async Task SubmitClarificationAsync(string executionId, string answer, CancellationToken ct = default)
    {
        if (_signalingStore is not null)
        {
            await _signalingStore.SubmitClarificationAsync(executionId, answer, ct);
            return;
        }

        ExecutionSession session = GetSession(executionId);
        session.ClarificationTcs?.TrySetResult(answer);
    }

    /// <summary>
    /// Blocks until a gate response is submitted, or times out.
    /// In distributed mode, polls the signaling store. In local mode, uses in-memory TCS.
    /// </summary>
    public async Task<GateResponse> WaitForGateResponseAsync(string executionId, CancellationToken ct)
    {
        await SaveCheckpointAsync(executionId, ExecutionStatus.Paused, PauseType.Gate, ct);

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromMinutes(_clarificationTimeoutMinutes));

        GateResponse result;

        if (_signalingStore is not null)
        {
            // Distributed mode: poll Cosmos for a gate response document
            result = await PollForGateResponseAsync(executionId, linked.Token);
        }
        else
        {
            // Local mode: in-memory TCS
            ExecutionSession session = GetSession(executionId);
            session.GateTcs = new TaskCompletionSource<GateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using CancellationTokenRegistration _ = linked.Token.Register(
                () => session.GateTcs.TrySetException(
                    new TimeoutException($"Gate approval timeout after {_clarificationTimeoutMinutes} minutes.")));

            result = await session.GateTcs.Task;
        }

        PauseType resumePause = result.Status == GateResponseStatus.SendBack ? PauseType.SendBack : PauseType.None;
        await SaveCheckpointAsync(executionId, ExecutionStatus.Running, resumePause, ct);
        return result;
    }

    /// <summary>
    /// Submits a gate response. In distributed mode, writes to the signaling store.
    /// In local mode, completes the in-memory TCS.
    /// </summary>
    public async Task SubmitGateResponseAsync(string executionId, GateResponse response, CancellationToken ct = default)
    {
        if (_signalingStore is not null)
        {
            await _signalingStore.SubmitGateResponseAsync(executionId, response, ct);
            return;
        }

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
    /// Notifies the waiting TCS for a clarification (used by ChangeFeedListenerService in distributed mode).
    /// </summary>
    public void NotifyClarification(string executionId, string answer)
    {
        if (_sessions.TryGetValue(executionId, out ExecutionSession? session))
        {
            session.ClarificationTcs?.TrySetResult(answer);
        }
    }

    /// <summary>
    /// Notifies the waiting TCS for a gate response (used by ChangeFeedListenerService in distributed mode).
    /// </summary>
    public void NotifyGateResponse(string executionId, GateResponse response)
    {
        if (_sessions.TryGetValue(executionId, out ExecutionSession? session))
        {
            session.GateTcs?.TrySetResult(response);
        }
    }

    private async Task<string> PollForClarificationAsync(string executionId, CancellationToken ct)
    {
        ExecutionSession session = GetSession(executionId);
        session.ClarificationTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register timeout
        await using CancellationTokenRegistration timeoutReg = ct.Register(
            () => session.ClarificationTcs.TrySetException(
                new TimeoutException($"Clarification timeout after {_clarificationTimeoutMinutes} minutes.")));

        // Start background polling (change feed listener may resolve it first)
        Task pollingTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !session.ClarificationTcs.Task.IsCompleted)
            {
                try
                {
                    string? answer = await _signalingStore!.TryGetClarificationAsync(executionId, ct);
                    if (answer is not null)
                    {
                        session.ClarificationTcs.TrySetResult(answer);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                await Task.Delay(_pollingIntervalMs, ct);
            }
        }, ct);

        return await session.ClarificationTcs.Task;
    }

    private async Task<GateResponse> PollForGateResponseAsync(string executionId, CancellationToken ct)
    {
        ExecutionSession session = GetSession(executionId);
        session.GateTcs = new TaskCompletionSource<GateResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register timeout
        await using CancellationTokenRegistration timeoutReg = ct.Register(
            () => session.GateTcs.TrySetException(
                new TimeoutException($"Gate approval timeout after {_clarificationTimeoutMinutes} minutes.")));

        // Start background polling (change feed listener may resolve it first)
        Task pollingTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !session.GateTcs.Task.IsCompleted)
            {
                try
                {
                    GateResponse? response = await _signalingStore!.TryGetGateResponseAsync(executionId, ct);
                    if (response is not null)
                    {
                        session.GateTcs.TrySetResult(response);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                await Task.Delay(_pollingIntervalMs, ct);
            }
        }, ct);

        return await session.GateTcs.Task;
    }

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
