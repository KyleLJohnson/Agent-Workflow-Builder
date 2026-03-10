using System.Collections.Concurrent;
using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace AgentWorkflowBuilder.Api.Hubs;

/// <summary>
/// SignalR hub for workflow execution.
/// When Service Bus is configured, enqueues execution requests for background processing.
/// Otherwise, falls back to in-process execution (local development).
/// </summary>
public class WorkflowHub : Hub
{
    private readonly IWorkflowEngine _engine;
    private readonly IWorkflowStore _workflowStore;
    private readonly ExecutionSessionManager _sessionManager;
    private readonly IExecutionQueue? _executionQueue;
    private readonly IConcurrencyCounter _concurrencyCounter;
    private readonly ILogger<WorkflowHub> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<WorkflowHub> _hubContext;
    private readonly int _maxConcurrentPerUser;

    // In-process execution tracking (used when Service Bus is not configured)
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> ActiveExecutions = new();
    private static readonly ConcurrentDictionary<string, int> UserExecutionCounts = new();

    public WorkflowHub(
        IWorkflowEngine engine,
        IWorkflowStore workflowStore,
        ExecutionSessionManager sessionManager,
        IConcurrencyCounter concurrencyCounter,
        ILogger<WorkflowHub> logger,
        IConfiguration configuration,
        IHubContext<WorkflowHub> hubContext,
        IExecutionQueue? executionQueue = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(concurrencyCounter);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(hubContext);
        _engine = engine;
        _workflowStore = workflowStore;
        _sessionManager = sessionManager;
        _executionQueue = executionQueue;
        _concurrencyCounter = concurrencyCounter;
        _logger = logger;
        _configuration = configuration;
        _hubContext = hubContext;
        _maxConcurrentPerUser = int.TryParse(
            configuration["Workflow:MaxConcurrentExecutionsPerUser"], out int val) ? val : 5;
    }

    /// <summary>
    /// Executes a workflow. When Service Bus is configured, enqueues to the execution queue.
    /// Otherwise, runs in-process for local development.
    /// </summary>
    [HubMethodName("ExecuteWorkflow")]
    public async Task<string> ExecuteWorkflowAsync(string workflowId, string inputMessage, bool? autoApproveGates = null)
    {
        string userId = GetUserId();

        // Check concurrency limit
        bool canExecute = await _concurrencyCounter.TryIncrementAsync(userId, _maxConcurrentPerUser);
        if (!canExecute)
        {
            await Clients.Caller.SendAsync("Error",
                $"Maximum concurrent executions ({_maxConcurrentPerUser}) reached. Please wait for a running execution to complete.",
                CancellationToken.None);
            return string.Empty;
        }

        string executionId = Guid.NewGuid().ToString();

        WorkflowDefinition? workflow = await _workflowStore.GetAsync(workflowId);
        if (workflow is null)
        {
            await Clients.Caller.SendAsync("Error", "Workflow not found.", CancellationToken.None);
            await _concurrencyCounter.DecrementAsync(userId);
            return string.Empty;
        }

        bool resolvedAutoApprove = autoApproveGates ?? workflow.AutoApproveGates;

        if (_executionQueue is not null)
        {
            // Distributed mode: enqueue to Service Bus for background worker processing
            ExecutionMessage message = new()
            {
                ExecutionId = executionId,
                WorkflowId = workflowId,
                UserId = userId,
                ConnectionId = Context.ConnectionId,
                InputMessage = inputMessage,
                AutoApproveGates = resolvedAutoApprove
            };

            await _executionQueue.EnqueueAsync(message);
            _logger.LogInformation("Enqueued execution {ExecutionId} for workflow {WorkflowId}", executionId, workflowId);
            return executionId;
        }

        // Local mode: in-process execution (same as before)
        return await ExecuteInProcessAsync(executionId, workflow, inputMessage, resolvedAutoApprove, userId);
    }

    /// <summary>
    /// Submits a clarification answer for a paused execution.
    /// </summary>
    [HubMethodName("AnswerClarification")]
    public async Task AnswerClarificationAsync(string executionId, string answer)
    {
        await _sessionManager.SubmitClarificationAsync(executionId, answer);
    }

    /// <summary>
    /// Approves a gate, optionally with edited output.
    /// </summary>
    [HubMethodName("ApproveGate")]
    public async Task ApproveGateAsync(string executionId, string? editedOutput)
    {
        await _sessionManager.SubmitGateResponseAsync(executionId, new GateResponse
        {
            Status = GateResponseStatus.Approved,
            EditedOutput = editedOutput
        });
    }

    /// <summary>
    /// Rejects a gate, aborting the workflow.
    /// </summary>
    [HubMethodName("RejectGate")]
    public async Task RejectGateAsync(string executionId, string? reason)
    {
        await _sessionManager.SubmitGateResponseAsync(executionId, new GateResponse
        {
            Status = GateResponseStatus.Rejected,
            Reason = reason
        });
    }

    /// <summary>
    /// Sends output back to a previous node for revision.
    /// </summary>
    [HubMethodName("SendBackGate")]
    public async Task SendBackGateAsync(string executionId, string? feedback)
    {
        await _sessionManager.SubmitGateResponseAsync(executionId, new GateResponse
        {
            Status = GateResponseStatus.SendBack,
            Feedback = feedback
        });
    }

    /// <summary>
    /// Cancels a running execution.
    /// In distributed mode, sends a cancellation message via Service Bus.
    /// In local mode, cancels the in-process CancellationTokenSource.
    /// </summary>
    [HubMethodName("CancelExecution")]
    public async Task CancelExecutionAsync(string executionId)
    {
        if (_executionQueue is not null)
        {
            await _executionQueue.EnqueueCancellationAsync(executionId);
            return;
        }

        // Local mode: cancel in-process
        if (ActiveExecutions.TryGetValue(executionId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
        }
    }

    /// <summary>
    /// Runs a workflow in-process (local development fallback when Service Bus is not configured).
    /// </summary>
    private async Task<string> ExecuteInProcessAsync(
        string executionId,
        WorkflowDefinition workflow,
        string inputMessage,
        bool autoApproveGates,
        string userId)
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(Context.ConnectionAborted);
        ActiveExecutions[executionId] = cts;
        CancellationToken ct = cts.Token;

        string connectionId = Context.ConnectionId;
        IClientProxy caller = _hubContext.Clients.Client(connectionId);

        _ = Task.Run(async () =>
        {
            try
            {
                await caller.SendAsync("ExecutionStarted", workflow.Id, ct);

                bool hasOutput = false;
                await foreach (WorkflowExecutionEvent evt in _engine.ExecuteStreamingAsync(workflow, inputMessage, autoApproveGates, executionId, ct))
                {
                    switch (evt.EventType)
                    {
                        case ExecutionEventType.AgentStepStarted:
                            await caller.SendAsync("AgentStepStarted", evt, ct);
                            break;
                        case ExecutionEventType.AgentStepCompleted:
                            await caller.SendAsync("AgentStepCompleted", evt, ct);
                            if (!string.IsNullOrWhiteSpace(evt.Data))
                                hasOutput = true;
                            break;
                        case ExecutionEventType.WorkflowOutput:
                            await caller.SendAsync("WorkflowOutput", evt, ct);
                            if (!string.IsNullOrWhiteSpace(evt.Data))
                                hasOutput = true;
                            break;
                        case ExecutionEventType.Error:
                            await caller.SendAsync("Error", evt.Data, ct);
                            break;
                        case ExecutionEventType.ClarificationNeeded:
                            await caller.SendAsync("ClarificationNeeded", evt, ct);
                            break;
                        case ExecutionEventType.GateAwaitingApproval:
                            await caller.SendAsync("GateAwaitingApproval", evt, ct);
                            break;
                        case ExecutionEventType.GateApproved:
                            await caller.SendAsync("GateApproved", evt, ct);
                            break;
                        case ExecutionEventType.GateRejected:
                            await caller.SendAsync("GateRejected", evt, ct);
                            break;
                        case ExecutionEventType.LoopIterationStarted:
                            await caller.SendAsync("LoopIterationStarted", evt, ct);
                            break;
                        case ExecutionEventType.LoopIterationCompleted:
                            await caller.SendAsync("LoopIterationCompleted", evt, ct);
                            break;
                        case ExecutionEventType.PlanGenerated:
                            await caller.SendAsync("PlanGenerated", evt, ct);
                            break;
                        case ExecutionEventType.PlanTriggered:
                            await caller.SendAsync("PlanTriggered", evt, ct);
                            break;
                        case ExecutionEventType.GateAutoApproved:
                            await caller.SendAsync("GateAutoApproved", evt, ct);
                            break;
                    }
                }

                if (!hasOutput)
                {
                    string provider = _configuration["CopilotSdk:Provider:Type"] ?? "(not set)";
                    string baseUrl = _configuration["CopilotSdk:Provider:BaseUrl"] ?? "(not set)";
                    string model = _configuration["CopilotSdk:DefaultModel"] ?? "(not set)";
                    _logger.LogWarning(
                        "Workflow {WorkflowId} completed but produced no output. CopilotSdk Provider={Provider}, BaseUrl={BaseUrl}, Model={Model}",
                        workflow.Id, provider, baseUrl, model);
                    await caller.SendAsync("WorkflowOutput", new WorkflowExecutionEvent
                    {
                        EventType = ExecutionEventType.WorkflowOutput,
                        ExecutionId = executionId,
                        Data = $"\u26a0\ufe0f The workflow completed but no AI output was generated. " +
                               $"Please verify your Copilot SDK configuration in appsettings.json \u2014 " +
                               $"Provider: {provider}, Base URL: {baseUrl}, Model: {model}"
                    }, CancellationToken.None);
                }

                await caller.SendAsync("ExecutionCompleted", workflow.Id, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or execution cancelled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow execution failed for {WorkflowId}", workflow.Id);
                await caller.SendAsync("Error", ex.Message, CancellationToken.None);
            }
            finally
            {
                DecrementUserCount(userId);
                ActiveExecutions.TryRemove(executionId, out CancellationTokenSource? removedCts);
                removedCts?.Dispose();
                await _concurrencyCounter.DecrementAsync(userId);
            }
        }, CancellationToken.None);

        return executionId;
    }

    /// <summary>
    /// Extracts the authenticated user's ID, falling back to ConnectionId when auth is not configured.
    /// </summary>
    private string GetUserId()
    {
        try
        {
            return UserContext.GetUserId(Context.User!);
        }
        catch (UnauthorizedAccessException)
        {
            return Context.ConnectionId;
        }
    }

    private static void DecrementUserCount(string userId)
    {
        UserExecutionCounts.AddOrUpdate(userId, 0, (_, c) => Math.Max(0, c - 1));
    }
}
