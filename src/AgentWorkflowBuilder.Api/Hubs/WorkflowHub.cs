using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace AgentWorkflowBuilder.Api.Hubs;

public class WorkflowHub : Hub
{
    private readonly IWorkflowEngine _engine;
    private readonly IWorkflowStore _workflowStore;
    private readonly ILogger<WorkflowHub> _logger;
    private readonly IConfiguration _configuration;

    public WorkflowHub(IWorkflowEngine engine, IWorkflowStore workflowStore, ILogger<WorkflowHub> logger, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(logger);
        _engine = engine;
        _workflowStore = workflowStore;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Executes a workflow by streaming agent step events back to the caller.
    /// </summary>
    [HubMethodName("ExecuteWorkflow")]
    public async Task ExecuteWorkflowAsync(string workflowId, string inputMessage)
    {
        var ct = Context.ConnectionAborted;
        var workflow = await _workflowStore.GetAsync(workflowId, ct);
        if (workflow is null)
        {
            await Clients.Caller.SendAsync("Error", "Workflow not found.", ct);
            return;
        }

        try
        {
            await Clients.Caller.SendAsync("ExecutionStarted", workflowId, ct);

            var hasOutput = false;
            await foreach (var evt in _engine.ExecuteStreamingAsync(workflow, inputMessage, ct))
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
                }
            }

            if (!hasOutput)
            {
                var provider = _configuration["CopilotSdk:Provider:Type"] ?? "(not set)";
                var baseUrl = _configuration["CopilotSdk:Provider:BaseUrl"] ?? "(not set)";
                var model = _configuration["CopilotSdk:DefaultModel"] ?? "(not set)";
                _logger.LogWarning("Workflow {WorkflowId} completed but produced no output. " +
                    "CopilotSdk Provider={Provider}, BaseUrl={BaseUrl}, Model={Model}",
                    workflowId, provider, baseUrl, model);
                await Clients.Caller.SendAsync("WorkflowOutput", new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.WorkflowOutput,
                    Data = $"⚠️ The workflow completed but no AI output was generated. " +
                           $"Please verify your Copilot SDK configuration in appsettings.json — " +
                           $"Provider: {provider}, Base URL: {baseUrl}, Model: {model}"
                }, ct);
            }

            await Clients.Caller.SendAsync("ExecutionCompleted", workflowId, ct);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — no further action needed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution failed for {WorkflowId}", workflowId);
            await Clients.Caller.SendAsync("Error", ex.Message, CancellationToken.None);
        }
    }
}
