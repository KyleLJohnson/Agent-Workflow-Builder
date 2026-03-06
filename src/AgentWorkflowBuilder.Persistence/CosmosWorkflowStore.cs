using System.Net;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace AgentWorkflowBuilder.Persistence;

/// <summary>
/// Cosmos DB implementation of <see cref="IWorkflowStore"/>.
/// Partition key: /userId
/// </summary>
public class CosmosWorkflowStore : IWorkflowStore
{
    private readonly CosmosClient _client;
    private readonly string _databaseName;
    private readonly string _containerName;
    private readonly ILogger<CosmosWorkflowStore> _logger;
    private Container? _container;

    public CosmosWorkflowStore(
        CosmosClient client,
        string databaseName,
        string containerName,
        ILogger<CosmosWorkflowStore> logger)
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

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(string? userId = null, CancellationToken ct = default)
    {
        Container container = await GetContainerAsync(ct);

        QueryDefinition query = userId is not null
            ? new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId ORDER BY c.updatedAt DESC")
                .WithParameter("@userId", userId)
            : new QueryDefinition("SELECT * FROM c ORDER BY c.updatedAt DESC");

        QueryRequestOptions? options = userId is not null
            ? new QueryRequestOptions { PartitionKey = new PartitionKey(userId) }
            : null;

        List<WorkflowDefinition> workflows = [];
        using FeedIterator<WorkflowDefinition> iterator = container.GetItemQueryIterator<WorkflowDefinition>(query, requestOptions: options);

        while (iterator.HasMoreResults)
        {
            FeedResponse<WorkflowDefinition> response = await iterator.ReadNextAsync(ct);
            workflows.AddRange(response);
        }

        return workflows;
    }

    public async Task<WorkflowDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Container container = await GetContainerAsync(ct);

        // Cross-partition query since we may not know the userId
        QueryDefinition query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
            .WithParameter("@id", id);

        using FeedIterator<WorkflowDefinition> iterator = container.GetItemQueryIterator<WorkflowDefinition>(query);
        while (iterator.HasMoreResults)
        {
            FeedResponse<WorkflowDefinition> response = await iterator.ReadNextAsync(ct);
            WorkflowDefinition? record = response.FirstOrDefault();
            if (record is not null)
                return record;
        }

        return null;
    }

    public async Task<WorkflowDefinition> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Container container = await GetContainerAsync(ct);

        WorkflowDefinition workflow = definition with
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        string partitionKey = workflow.UserId ?? string.Empty;
        await container.CreateItemAsync(workflow, new PartitionKey(partitionKey), cancellationToken: ct);
        return workflow;
    }

    public async Task<WorkflowDefinition> UpdateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Container container = await GetContainerAsync(ct);

        // Fetch existing to verify it exists and get the partition key
        WorkflowDefinition? existing = await GetAsync(definition.Id, ct)
            ?? throw new FileNotFoundException($"Workflow '{definition.Id}' not found.");

        WorkflowDefinition updated = definition with
        {
            UserId = existing.UserId,
            UpdatedAt = DateTime.UtcNow
        };

        string partitionKey = updated.UserId ?? string.Empty;
        await container.ReplaceItemAsync(updated, updated.Id, new PartitionKey(partitionKey), cancellationToken: ct);
        return updated;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Container container = await GetContainerAsync(ct);

        // Fetch to get partition key
        WorkflowDefinition? existing = await GetAsync(id, ct)
            ?? throw new FileNotFoundException($"Workflow '{id}' not found.");

        string partitionKey = existing.UserId ?? string.Empty;
        await container.DeleteItemAsync<WorkflowDefinition>(id, new PartitionKey(partitionKey), cancellationToken: ct);
    }

    private async Task<Container> GetContainerAsync(CancellationToken ct)
    {
        if (_container is not null)
            return _container;

        Database database = await _client.CreateDatabaseIfNotExistsAsync(_databaseName, cancellationToken: ct);
        ContainerProperties properties = new(_containerName, "/userId");
        ContainerResponse containerResponse = await database.CreateContainerIfNotExistsAsync(properties, cancellationToken: ct);
        _container = containerResponse.Container;

        _logger.LogInformation("Cosmos workflow store initialized: {Database}/{Container}", _databaseName, _containerName);
        return _container;
    }
}
