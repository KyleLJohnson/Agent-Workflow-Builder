using System.Net;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace AgentWorkflowBuilder.Persistence;

/// <summary>
/// Cosmos DB implementation of <see cref="ISessionSignalingStore"/> for distributed gate/clarification signaling.
/// Partition key: /executionId. TTL enabled for automatic cleanup.
/// Change feed listeners watch this container to unblock waiting workers.
/// </summary>
public class CosmosSessionStore : ISessionSignalingStore
{
    private readonly CosmosClient _client;
    private readonly string _databaseName;
    private readonly string _containerName;
    private readonly ILogger<CosmosSessionStore> _logger;
    private Container? _container;

    public CosmosSessionStore(
        CosmosClient client,
        string databaseName,
        string containerName,
        ILogger<CosmosSessionStore> logger)
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

    public async Task SubmitClarificationAsync(string executionId, string answer, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);

        Container container = await GetContainerAsync(ct);
        SessionDocument doc = new()
        {
            Id = $"clarification-{executionId}-{DateTime.UtcNow.Ticks}",
            ExecutionId = executionId,
            DocumentType = SessionDocumentType.ClarificationResponse,
            ClarificationAnswer = answer
        };

        await container.CreateItemAsync(doc, new PartitionKey(executionId), cancellationToken: ct);
        _logger.LogInformation("Stored clarification response for execution {ExecutionId}", executionId);
    }

    public async Task SubmitGateResponseAsync(string executionId, GateResponse response, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentNullException.ThrowIfNull(response);

        Container container = await GetContainerAsync(ct);
        SessionDocument doc = new()
        {
            Id = $"gate-{executionId}-{DateTime.UtcNow.Ticks}",
            ExecutionId = executionId,
            DocumentType = SessionDocumentType.GateResponse,
            GateResponse = response
        };

        await container.CreateItemAsync(doc, new PartitionKey(executionId), cancellationToken: ct);
        _logger.LogInformation("Stored gate response ({Status}) for execution {ExecutionId}", response.Status, executionId);
    }

    public async Task<string?> TryGetClarificationAsync(string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        Container container = await GetContainerAsync(ct);
        QueryDefinition query = new QueryDefinition(CosmosQueries.LatestClarification)
            .WithParameter("@execId", executionId);

        using FeedIterator<SessionDocument> iterator = container.GetItemQueryIterator<SessionDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(executionId) });

        while (iterator.HasMoreResults)
        {
            FeedResponse<SessionDocument> response = await iterator.ReadNextAsync(ct);
            SessionDocument? doc = response.FirstOrDefault();
            if (doc is not null)
                return doc.ClarificationAnswer;
        }

        return null;
    }

    public async Task<GateResponse?> TryGetGateResponseAsync(string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        Container container = await GetContainerAsync(ct);
        QueryDefinition query = new QueryDefinition(CosmosQueries.LatestGateResponse)
            .WithParameter("@execId", executionId);

        using FeedIterator<SessionDocument> iterator = container.GetItemQueryIterator<SessionDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(executionId) });

        while (iterator.HasMoreResults)
        {
            FeedResponse<SessionDocument> response = await iterator.ReadNextAsync(ct);
            SessionDocument? doc = response.FirstOrDefault();
            if (doc is not null)
                return doc.GateResponse;
        }

        return null;
    }

    /// <summary>
    /// Gets the raw Cosmos container for use by change feed listeners.
    /// </summary>
    public async Task<Container> GetContainerForChangeFeedAsync(CancellationToken ct = default)
        => await GetContainerAsync(ct);

    private async Task<Container> GetContainerAsync(CancellationToken ct)
    {
        if (_container is not null)
            return _container;

        Database database = await _client.CreateDatabaseIfNotExistsAsync(_databaseName, cancellationToken: ct);
        ContainerProperties properties = new(_containerName, "/executionId")
        {
            DefaultTimeToLive = 86400 // 24 hours
        };
        ContainerResponse containerResponse = await database.CreateContainerIfNotExistsAsync(properties, cancellationToken: ct);
        _container = containerResponse.Container;

        _logger.LogInformation("Cosmos session store initialized: {Database}/{Container}", _databaseName, _containerName);
        return _container;
    }
}
