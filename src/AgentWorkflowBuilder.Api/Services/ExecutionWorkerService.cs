using System.Text.Json;
using AgentWorkflowBuilder.Api.Hubs;
using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.SignalR;

namespace AgentWorkflowBuilder.Api.Services;

/// <summary>
/// Background worker that dequeues execution requests from Azure Service Bus,
/// runs them via <see cref="IWorkflowEngine"/>, and pushes events to clients via Azure SignalR.
/// Also listens on a cancellation queue to cancel running executions.
/// </summary>
internal sealed class ExecutionWorkerService : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IWorkflowEngine _engine;
    private readonly IWorkflowStore _workflowStore;
    private readonly IExecutionStore _executionStore;
    private readonly ExecutionSessionManager _sessionManager;
    private readonly IConcurrencyCounter _concurrencyCounter;
    private readonly CancellationManager _cancellationManager;
    private readonly IHubContext<WorkflowHub> _hubContext;
    private readonly ILogger<ExecutionWorkerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _executionQueueName;
    private readonly string _cancellationQueueName;
    private readonly int _maxConcurrentPerUser;

    public ExecutionWorkerService(
        ServiceBusClient serviceBusClient,
        IWorkflowEngine engine,
        IWorkflowStore workflowStore,
        IExecutionStore executionStore,
        ExecutionSessionManager sessionManager,
        IConcurrencyCounter concurrencyCounter,
        CancellationManager cancellationManager,
        IHubContext<WorkflowHub> hubContext,
        ILogger<ExecutionWorkerService> logger,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(serviceBusClient);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(workflowStore);
        ArgumentNullException.ThrowIfNull(executionStore);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(concurrencyCounter);
        ArgumentNullException.ThrowIfNull(cancellationManager);
        ArgumentNullException.ThrowIfNull(hubContext);
        ArgumentNullException.ThrowIfNull(logger);
        _serviceBusClient = serviceBusClient;
        _engine = engine;
        _workflowStore = workflowStore;
        _executionStore = executionStore;
        _sessionManager = sessionManager;
        _concurrencyCounter = concurrencyCounter;
        _cancellationManager = cancellationManager;
        _hubContext = hubContext;
        _logger = logger;
        _configuration = configuration;
        _executionQueueName = configuration["ServiceBus:ExecutionQueueName"] ?? "workflow-executions";
        _cancellationQueueName = configuration["ServiceBus:CancellationQueueName"] ?? "execution-cancellations";
        _maxConcurrentPerUser = int.TryParse(
            configuration["Workflow:MaxConcurrentExecutionsPerUser"], out int val) ? val : 5;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start both listeners concurrently
        Task executionListener = ListenForExecutionsAsync(stoppingToken);
        Task cancellationListener = ListenForCancellationsAsync(stoppingToken);

        await Task.WhenAll(executionListener, cancellationListener);
    }

    private async Task ListenForExecutionsAsync(CancellationToken stoppingToken)
    {
        ServiceBusProcessor processor = _serviceBusClient.CreateProcessor(
            _executionQueueName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 5,
                AutoCompleteMessages = false,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(30)
            });

        processor.ProcessMessageAsync += async (ProcessMessageEventArgs args) =>
        {
            try
            {
                await ProcessExecutionMessageAsync(args, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing execution message");
            }
        };

        processor.ProcessErrorAsync += (ProcessErrorEventArgs args) =>
        {
            _logger.LogError(args.Exception, "Service Bus processor error: {Source}", args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);

        // Wait until shutdown
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }

        await processor.StopProcessingAsync();
        await processor.DisposeAsync();
    }

    private async Task ProcessExecutionMessageAsync(ProcessMessageEventArgs args, CancellationToken stoppingToken)
    {
        ExecutionMessage? message = JsonSerializer.Deserialize<ExecutionMessage>(args.Message.Body.ToString());
        if (message is null)
        {
            _logger.LogWarning("Received null execution message, dead-lettering");
            await args.DeadLetterMessageAsync(args.Message, "Invalid message format");
            return;
        }

        _logger.LogInformation("Processing execution {ExecutionId} for workflow {WorkflowId}",
            message.ExecutionId, message.WorkflowId);

        WorkflowDefinition? workflow = await _workflowStore.GetAsync(message.WorkflowId, stoppingToken);
        if (workflow is null)
        {
            _logger.LogWarning("Workflow {WorkflowId} not found, dead-lettering", message.WorkflowId);
            await args.DeadLetterMessageAsync(args.Message, "Workflow not found");
            await _concurrencyCounter.DecrementAsync(message.UserId, stoppingToken);
            return;
        }

        IClientProxy caller = _hubContext.Clients.Client(message.ConnectionId);
        CancellationTokenSource cts = _cancellationManager.CreateLinkedToken(message.ExecutionId, stoppingToken);

        try
        {
            await caller.SendAsync("ExecutionStarted", message.WorkflowId, cts.Token);

            bool hasOutput = false;
            await foreach (WorkflowExecutionEvent evt in _engine.ExecuteStreamingAsync(
                workflow, message.InputMessage, message.AutoApproveGates, message.ExecutionId, cts.Token))
            {
                await SendEventToClientAsync(caller, evt, cts.Token);

                if (evt.EventType == ExecutionEventType.WorkflowOutput && !string.IsNullOrWhiteSpace(evt.Data))
                    hasOutput = true;
            }

            if (!hasOutput)
            {
                string provider = _configuration["CopilotSdk:Provider:Type"] ?? "(not set)";
                string baseUrl = _configuration["CopilotSdk:Provider:BaseUrl"] ?? "(not set)";
                string model = _configuration["CopilotSdk:DefaultModel"] ?? "(not set)";
                _logger.LogWarning(
                    "Workflow {WorkflowId} completed but produced no output. CopilotSdk Provider={Provider}, BaseUrl={BaseUrl}, Model={Model}",
                    message.WorkflowId, provider, baseUrl, model);
                await caller.SendAsync("WorkflowOutput", new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.WorkflowOutput,
                    ExecutionId = message.ExecutionId,
                    Data = $"\u26a0\ufe0f The workflow completed but no AI output was generated. " +
                           $"Please verify your Copilot SDK configuration \u2014 " +
                           $"Provider: {provider}, Base URL: {baseUrl}, Model: {model}"
                }, CancellationToken.None);
            }

            await caller.SendAsync("ExecutionCompleted", message.WorkflowId, CancellationToken.None);
            await args.CompleteMessageAsync(args.Message);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Execution {ExecutionId} was cancelled", message.ExecutionId);
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execution {ExecutionId} failed", message.ExecutionId);
            await caller.SendAsync("Error", ex.Message, CancellationToken.None);
            await args.CompleteMessageAsync(args.Message);
        }
        finally
        {
            _cancellationManager.Remove(message.ExecutionId);
            await _concurrencyCounter.DecrementAsync(message.UserId, stoppingToken);
        }
    }

    private static async Task SendEventToClientAsync(IClientProxy caller, WorkflowExecutionEvent evt, CancellationToken ct)
    {
        string methodName = evt.EventType switch
        {
            ExecutionEventType.AgentStepStarted => "AgentStepStarted",
            ExecutionEventType.AgentStepCompleted => "AgentStepCompleted",
            ExecutionEventType.WorkflowOutput => "WorkflowOutput",
            ExecutionEventType.Error => "Error",
            ExecutionEventType.ClarificationNeeded => "ClarificationNeeded",
            ExecutionEventType.GateAwaitingApproval => "GateAwaitingApproval",
            ExecutionEventType.GateApproved => "GateApproved",
            ExecutionEventType.GateRejected => "GateRejected",
            ExecutionEventType.LoopIterationStarted => "LoopIterationStarted",
            ExecutionEventType.LoopIterationCompleted => "LoopIterationCompleted",
            ExecutionEventType.PlanGenerated => "PlanGenerated",
            ExecutionEventType.PlanTriggered => "PlanTriggered",
            ExecutionEventType.GateAutoApproved => "GateAutoApproved",
            _ => "WorkflowOutput"
        };

        if (evt.EventType == ExecutionEventType.Error)
        {
            await caller.SendAsync(methodName, evt.Data, ct);
        }
        else
        {
            await caller.SendAsync(methodName, evt, ct);
        }
    }

    private async Task ListenForCancellationsAsync(CancellationToken stoppingToken)
    {
        ServiceBusProcessor processor = _serviceBusClient.CreateProcessor(
            _cancellationQueueName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 10,
                AutoCompleteMessages = false
            });

        processor.ProcessMessageAsync += async (ProcessMessageEventArgs args) =>
        {
            string executionId = args.Message.Body.ToString();
            _logger.LogInformation("Received cancellation for execution {ExecutionId}", executionId);
            _cancellationManager.Cancel(executionId);
            await args.CompleteMessageAsync(args.Message);
        };

        processor.ProcessErrorAsync += (ProcessErrorEventArgs args) =>
        {
            _logger.LogError(args.Exception, "Cancellation processor error: {Source}", args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }

        await processor.StopProcessingAsync();
        await processor.DisposeAsync();
    }
}
