using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Api.Services;

/// <summary>
/// On startup, checks for paused executions in the execution store and
/// re-creates their sessions so they can be resumed when a client reconnects.
/// </summary>
internal sealed class ExecutionRecoveryService : IHostedService
{
    private readonly IExecutionStore _executionStore;
    private readonly ExecutionSessionManager _sessionManager;
    private readonly ILogger<ExecutionRecoveryService> _logger;

    public ExecutionRecoveryService(
        IExecutionStore executionStore,
        ExecutionSessionManager sessionManager,
        ILogger<ExecutionRecoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(executionStore);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(logger);
        _executionStore = executionStore;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ExecutionRecord> paused = await _executionStore.GetPausedAsync(cancellationToken);

            if (paused.Count == 0)
            {
                _logger.LogInformation("No paused executions to recover");
                return;
            }

            _logger.LogInformation("Found {Count} paused execution(s) to recover", paused.Count);

            foreach (ExecutionRecord record in paused)
            {
                if (_sessionManager.HasSession(record.Id))
                    continue;

                try
                {
                    _sessionManager.CreateSession(record.Id);
                    _logger.LogInformation(
                        "Recovered session for execution {ExecutionId} (workflow {WorkflowId}, pause type: {PauseType})",
                        record.Id, record.WorkflowId, record.PauseType);
                }
                catch (InvalidOperationException)
                {
                    // Session already exists, skip
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Execution recovery failed — paused executions may require manual attention");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
