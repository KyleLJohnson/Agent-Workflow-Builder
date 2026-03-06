using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AgentWorkflowBuilder.Api.Services;

/// <summary>
/// Background service that polls Azure Blob Storage containers for plan documents
/// and triggers workflow execution for each new blob.
/// </summary>
internal sealed class BlobPlanPollingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BlobServiceClient _blobClient;
    private readonly ILogger<BlobPlanPollingService> _logger;
    private readonly int _pollingIntervalSeconds;

    public BlobPlanPollingService(
        IServiceProvider serviceProvider,
        BlobServiceClient blobClient,
        IConfiguration configuration,
        ILogger<BlobPlanPollingService> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(blobClient);
        ArgumentNullException.ThrowIfNull(logger);
        _serviceProvider = serviceProvider;
        _blobClient = blobClient;
        _logger = logger;
        _pollingIntervalSeconds = int.TryParse(
            configuration["AzureBlobPlans:PollingIntervalSeconds"], out int val) ? val : 30;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BlobPlanPollingService started, polling every {Seconds}s", _pollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllContainersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during blob polling cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_pollingIntervalSeconds), stoppingToken);
        }
    }

    private async Task PollAllContainersAsync(CancellationToken ct)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        IWorkflowStore workflowStore = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
        IWorkflowEngine engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();

        IReadOnlyList<WorkflowDefinition> workflows = await workflowStore.ListAsync(userId: null, ct);

        foreach (WorkflowDefinition workflow in workflows)
        {
            if (string.IsNullOrWhiteSpace(workflow.BlobContainerName))
                continue;

            ct.ThrowIfCancellationRequested();

            try
            {
                await PollContainerAsync(workflow, engine, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling container '{Container}' for workflow {WorkflowId}",
                    workflow.BlobContainerName, workflow.Id);
            }
        }
    }

    private async Task PollContainerAsync(WorkflowDefinition workflow, IWorkflowEngine engine, CancellationToken ct)
    {
        BlobContainerClient container = _blobClient.GetBlobContainerClient(workflow.BlobContainerName);
        if (!await container.ExistsAsync(ct))
            return;

        await foreach (BlobItem blob in container.GetBlobsAsync(
            traits: BlobTraits.Metadata, cancellationToken: ct))
        {
            // Skip already-processed blobs
            if (blob.Metadata?.ContainsKey("processed") == true)
                continue;

            // Skip blobs in the _processed subfolder
            if (blob.Name.StartsWith("_processed/", StringComparison.OrdinalIgnoreCase))
                continue;

            ct.ThrowIfCancellationRequested();

            try
            {
                BlobClient blobClient = container.GetBlobClient(blob.Name);
                BlobDownloadResult download = await blobClient.DownloadContentAsync(ct);
                string content = download.Content.ToString();

                _logger.LogInformation("Processing blob '{BlobName}' for workflow {WorkflowId}",
                    blob.Name, workflow.Id);

                await foreach (WorkflowExecutionEvent evt in engine.ExecuteStreamingAsync(workflow, content, ct: ct))
                {
                    // Log execution events for blob-triggered runs (no SignalR client)
                    if (evt.EventType == ExecutionEventType.Error)
                    {
                        _logger.LogWarning("Blob execution error for '{BlobName}': {Error}",
                            blob.Name, evt.Data);
                    }
                }

                // Mark as processed by moving to _processed subfolder
                BlobClient processedBlob = container.GetBlobClient($"_processed/{blob.Name}");
                await processedBlob.StartCopyFromUriAsync(blobClient.Uri, cancellationToken: ct);
                await blobClient.DeleteAsync(cancellationToken: ct);

                _logger.LogInformation("Successfully processed blob '{BlobName}'", blob.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process blob '{BlobName}' for workflow {WorkflowId}",
                    blob.Name, workflow.Id);
            }
        }
    }
}
