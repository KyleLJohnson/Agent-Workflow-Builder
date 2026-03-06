using System.Collections.Concurrent;
using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace AgentWorkflowBuilder.Api.Hubs;

public class WorkflowHub : Hub
{
    private readonly IWorkflowEngine _engine;
    private readonly IWorkflowStore _workflowStore;
    private readonly ExecutionSessionManager _sessionManager;
    private readonly ILogger<WorkflowHub> _logger;
    private readonly IConfiguration _configuration;
    private readonly int _maxConcurrentPerUser;

    private static readonly ConcurrentDictionary<string, CancellationTokenSource> ActiveExecutions = new();
    private static readonly ConcurrentDictionary<string, int> UserExecutionCounts = new();

    public WorkflowHub(
        IWorkflowEngine engine,
        IWorkflowStore workflowStore,
        ExecutionSessionManager sessionManager,
        ILogger<WorkflowHub> logger,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(logger);
        _engine = engine;
        _workflowStore = workflowStore;
        _sessionManager = sessionManager;
        _logger = logger;
        _configuration = configuration;
        _maxConcurrentPerUser = int.TryParse(
            configuration["Workflow:MaxConcurrentExecutionsPerUser"], out int val) ? val : 5;
    }

    /// <summary>
    /// Executes a workflow by streaming agent step events back to the caller.
    /// Returns the executionId for tracking.
    /// </summary>
    [HubMethodName("ExecuteWorkflow")]
    public async Task<string> ExecuteWorkflowAsync(string workflowId, string inputMessage, bool? autoApproveGates = null)
    {
        string userId = GetUserId();
        int currentCount = UserExecutionCounts.GetOrAdd(userId, 0);
        if (currentCount >= _maxConcurrentPerUser)
        {
            await Clients.Caller.SendAsync("Error",
                $"Maximum concurrent executions ({_maxConcurrentPerUser}) reached. Please wait for a running execution to complete.",
                CancellationToken.None);
            return string.Empty;
        }

        UserExecutionCounts.AddOrUpdate(userId, 1, (_, c) => c + 1);

        string executionId = Guid.NewGuid().ToString();
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(Context.ConnectionAborted);
        ActiveExecutions[executionId] = cts;
        CancellationToken ct = cts.Token;

        WorkflowDefinition? workflow = await _workflowStore.GetAsync(workflowId, ct);
        if (workflow is null)
        {
            await Clients.Caller.SendAsync("Error", "Workflow not found.", ct);
            DecrementUserCount(userId);
            ActiveExecutions.TryRemove(executionId, out _);
            return string.Empty;
        }

        // Fire and forget the execution, sending events as they arrive
        _ = Task.Run(async () =>
        {
            try
            {
                await Clients.Caller.SendAsync("ExecutionStarted", workflowId, ct);

                bool resolvedAutoApprove = autoApproveGates ?? workflow.AutoApproveGates;
                bool hasOutput = false;
                await foreach (WorkflowExecutionEvent evt in _engine.ExecuteStreamingAsync(workflow, inputMessage, resolvedAutoApprove, ct))
                {
                    switch (evt.EventType)
                    {
                        case ExecutionEventType.AgentStepStarted:
                            await Clients.Caller.SendAsync("AgentStepStarted", evt, ct);
                            break;
                        case ExecutionEventType.AgentStepCompleted:
                            await Clients.Caller.SendAsync("AgentStepCompleted", evt, ct);
                            if (!string.IsNullOrWhiteSpace(evt.Data))
                                hasOutput = true;
                            break;
                        case ExecutionEventType.WorkflowOutput:
                            await Clients.Caller.SendAsync("WorkflowOutput", evt, ct);
                            if (!string.IsNullOrWhiteSpace(evt.Data))
                                hasOutput = true;
                            break;
                        case ExecutionEventType.Error:
                            await Clients.Caller.SendAsync("Error", evt.Data, ct);
                            break;
                        case ExecutionEventType.ClarificationNeeded:
                            await Clients.Caller.SendAsync("ClarificationNeeded", evt, ct);
                            break;
                        case ExecutionEventType.GateAwaitingApproval:
                            await Clients.Caller.SendAsync("GateAwaitingApproval", evt, ct);
                            break;
                        case ExecutionEventType.GateApproved:
                            await Clients.Caller.SendAsync("GateApproved", evt, ct);
                            break;
                        case ExecutionEventType.GateRejected:
                            await Clients.Caller.SendAsync("GateRejected", evt, ct);
                            break;
                        case ExecutionEventType.LoopIterationStarted:
                            await Clients.Caller.SendAsync("LoopIterationStarted", evt, ct);
                            break;
                        case ExecutionEventType.LoopIterationCompleted:
                            await Clients.Caller.SendAsync("LoopIterationCompleted", evt, ct);
                            break;
                        case ExecutionEventType.PlanGenerated:
                            await Clients.Caller.SendAsync("PlanGenerated", evt, ct);
                            break;
                        case ExecutionEventType.PlanTriggered:
                            await Clients.Caller.SendAsync("PlanTriggered", evt, ct);
                            break;
                        case ExecutionEventType.GateAutoApproved:
                            await Clients.Caller.SendAsync("GateAutoApproved", evt, ct);
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
                        workflowId, provider, baseUrl, model);
                    await Clients.Caller.SendAsync("WorkflowOutput", new WorkflowExecutionEvent
                    {
                        EventType = ExecutionEventType.WorkflowOutput,
                        ExecutionId = executionId,
                        Data = $"⚠️ The workflow completed but no AI output was generated. " +
                               $"Please verify your Copilot SDK configuration in appsettings.json — " +
                               $"Provider: {provider}, Base URL: {baseUrl}, Model: {model}"
                    }, CancellationToken.None);
                }

                await Clients.Caller.SendAsync("ExecutionCompleted", workflowId, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or execution cancelled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow execution failed for {WorkflowId}", workflowId);
                await Clients.Caller.SendAsync("Error", ex.Message, CancellationToken.None);
            }
            finally
            {
                DecrementUserCount(userId);
                ActiveExecutions.TryRemove(executionId, out CancellationTokenSource? removedCts);
                removedCts?.Dispose();
            }
        }, CancellationToken.None);

        return executionId;
    }

    /// <summary>
    /// Submits a clarification answer for a paused execution.
    /// </summary>
    [HubMethodName("AnswerClarification")]
    public void AnswerClarification(string executionId, string answer)
    {
        _sessionManager.SubmitClarification(executionId, answer);
    }

    /// <summary>
    /// Approves a gate, optionally with edited output.
    /// </summary>
    [HubMethodName("ApproveGate")]
    public void ApproveGate(string executionId, string? editedOutput)
    {
        _sessionManager.SubmitGateResponse(executionId, new GateResponse
        {
            Status = GateResponseStatus.Approved,
            EditedOutput = editedOutput
        });
    }

    /// <summary>
    /// Rejects a gate, aborting the workflow.
    /// </summary>
    [HubMethodName("RejectGate")]
    public void RejectGate(string executionId, string? reason)
    {
        _sessionManager.SubmitGateResponse(executionId, new GateResponse
        {
            Status = GateResponseStatus.Rejected,
            Reason = reason
        });
    }

    /// <summary>
    /// Sends output back to a previous node for revision.
    /// </summary>
    [HubMethodName("SendBackGate")]
    public void SendBackGate(string executionId, string? feedback)
    {
        _sessionManager.SubmitGateResponse(executionId, new GateResponse
        {
            Status = GateResponseStatus.SendBack,
            Feedback = feedback
        });
    }

    /// <summary>
    /// Cancels a running execution.
    /// </summary>
    [HubMethodName("CancelExecution")]
    public void CancelExecution(string executionId)
    {
        if (ActiveExecutions.TryGetValue(executionId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
        }
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
            // Auth not configured — fall back to connection ID for local dev
            return Context.ConnectionId;
        }
    }

    private static void DecrementUserCount(string userId)
    {
        UserExecutionCounts.AddOrUpdate(userId, 0, (_, c) => Math.Max(0, c - 1));
    }
}
