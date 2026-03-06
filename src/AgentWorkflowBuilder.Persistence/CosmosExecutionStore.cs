using System.Net;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace AgentWorkflowBuilder.Persistence;

/// <summary>
/// Cosmos DB implementation of <see cref="IExecutionStore"/> for durable execution persistence.
/// Partition key: /workflowId
/// </summary>
public class CosmosExecutionStore : IExecutionStore
{
    private readonly CosmosClient _client;
    private readonly string _databaseName;
    private readonly string _containerName;
    private readonly ILogger<CosmosExecutionStore> _logger;
    private Container? _container;

    public CosmosExecutionStore(
        CosmosClient client,
        string databaseName,
        string containerName,
        ILogger<CosmosExecutionStore> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _databaseName = databaseName;
        _containerName = containerName;
        _logger = logger;
    }

    public async Task SaveAsync(ExecutionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        Container container = await GetContainerAsync(ct);
        await container.UpsertItemAsync(record, new PartitionKey(record.WorkflowId), cancellationToken: ct);
    }

    public async Task<ExecutionRecord?> GetAsync(string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        Container container = await GetContainerAsync(ct);

        // Cross-partition query since we don't know the workflowId
        QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
            .WithParameter("@id", executionId);

        using FeedIterator<ExecutionRecord> iterator = container.GetItemQueryIterator<ExecutionRecord>(query);
        while (iterator.HasMoreResults)
        {
            FeedResponse<ExecutionRecord> response = await iterator.ReadNextAsync(ct);
            ExecutionRecord? record = response.FirstOrDefault();
            if (record is not null)
                return record;
        }

        return null;
    }

    public async Task<IReadOnlyList<ExecutionRecord>> ListByWorkflowAsync(string workflowId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        Container container = await GetContainerAsync(ct);

        QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.workflowId = @wfId ORDER BY c.startedAt DESC")
            .WithParameter("@wfId", workflowId);

        List<ExecutionRecord> records = [];
        using FeedIterator<ExecutionRecord> iterator = container.GetItemQueryIterator<ExecutionRecord>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(workflowId) });

        while (iterator.HasMoreResults)
        {
            FeedResponse<ExecutionRecord> response = await iterator.ReadNextAsync(ct);
            records.AddRange(response);
        }

        return records;
    }

    public async Task<IReadOnlyList<ExecutionRecord>> GetPausedAsync(CancellationToken ct = default)
    {
        Container container = await GetContainerAsync(ct);

        QueryDefinition query = new("SELECT * FROM c WHERE c.status = 'Paused'");

        List<ExecutionRecord> paused = [];
        using FeedIterator<ExecutionRecord> iterator = container.GetItemQueryIterator<ExecutionRecord>(query);

        while (iterator.HasMoreResults)
        {
            FeedResponse<ExecutionRecord> response = await iterator.ReadNextAsync(ct);
            paused.AddRange(response);
        }

        return paused;
    }

    private async Task<Container> GetContainerAsync(CancellationToken ct)
    {
        if (_container is not null)
            return _container;

        Database database = await _client.CreateDatabaseIfNotExistsAsync(_databaseName, cancellationToken: ct);
        ContainerProperties properties = new(_containerName, "/workflowId");
        ContainerResponse containerResponse = await database.CreateContainerIfNotExistsAsync(properties, cancellationToken: ct);
        _container = containerResponse.Container;

        _logger.LogInformation("Cosmos execution store initialized: {Database}/{Container}", _databaseName, _containerName);
        return _container;
    }
}
