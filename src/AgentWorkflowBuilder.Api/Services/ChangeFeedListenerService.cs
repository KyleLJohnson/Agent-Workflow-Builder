using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Models;
using AgentWorkflowBuilder.Persistence;
using Microsoft.Azure.Cosmos;

namespace AgentWorkflowBuilder.Api.Services;

/// <summary>
/// Background service that listens to the Cosmos DB change feed on the sessions container
/// to detect gate/clarification responses and unblock waiting workers.
/// This provides near-real-time signaling without polling.
/// </summary>
internal sealed class ChangeFeedListenerService : BackgroundService
{
    private readonly CosmosSessionStore _sessionStore;
    private readonly ExecutionSessionManager _sessionManager;
    private readonly ILogger<ChangeFeedListenerService> _logger;
    private readonly string _leaseContainerName;

    public ChangeFeedListenerService(
        CosmosSessionStore sessionStore,
        ExecutionSessionManager sessionManager,
        ILogger<ChangeFeedListenerService> logger,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(logger);
        _sessionStore = sessionStore;
        _sessionManager = sessionManager;
        _logger = logger;
        _leaseContainerName = configuration["CosmosDb:LeaseContainerName"] ?? "session-leases";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Container sessionsContainer = await _sessionStore.GetContainerForChangeFeedAsync(stoppingToken);
            Database database = sessionsContainer.Database;

            // Create lease container for change feed processor
            Container leaseContainer = await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(_leaseContainerName, "/id"),
                cancellationToken: stoppingToken);

            string processorName = $"session-listener-{Environment.MachineName}";
            ChangeFeedProcessor processor = sessionsContainer
                .GetChangeFeedProcessorBuilder<SessionDocument>(processorName, HandleChangesAsync)
                .WithInstanceName(Environment.MachineName)
                .WithLeaseContainer(leaseContainer)
                .WithStartTime(DateTime.UtcNow)
                .Build();

            await processor.StartAsync();
            _logger.LogInformation("Change feed listener started for session signaling");

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down
            }

            await processor.StopAsync();
            _logger.LogInformation("Change feed listener stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change feed listener failed to start — falling back to polling mode");
        }
    }

    private Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<SessionDocument> changes,
        CancellationToken ct)
    {
        foreach (SessionDocument doc in changes)
        {
            try
            {
                switch (doc.DocumentType)
                {
                    case SessionDocumentType.ClarificationResponse
                        when !string.IsNullOrWhiteSpace(doc.ClarificationAnswer):
                        _logger.LogInformation(
                            "Change feed: clarification response for execution {ExecutionId}", doc.ExecutionId);
                        _sessionManager.NotifyClarification(doc.ExecutionId, doc.ClarificationAnswer);
                        break;

                    case SessionDocumentType.GateResponse
                        when doc.GateResponse is not null:
                        _logger.LogInformation(
                            "Change feed: gate response ({Status}) for execution {ExecutionId}",
                            doc.GateResponse.Status, doc.ExecutionId);
                        _sessionManager.NotifyGateResponse(doc.ExecutionId, doc.GateResponse);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing change feed document {DocId}", doc.Id);
            }
        }

        return Task.CompletedTask;
    }
}
