using System.Net;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace AgentWorkflowBuilder.Persistence;

/// <summary>
/// Cosmos DB implementation of <see cref="IConcurrencyCounter"/> using optimistic concurrency (ETag).
/// Partition key: /userId
/// </summary>
public class CosmosConcurrencyCounter : IConcurrencyCounter
{
    private readonly CosmosClient _client;
    private readonly string _databaseName;
    private readonly string _containerName;
    private readonly ILogger<CosmosConcurrencyCounter> _logger;
    private Container? _container;

    public CosmosConcurrencyCounter(
        CosmosClient client,
        string databaseName,
        string containerName,
        ILogger<CosmosConcurrencyCounter> logger)
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

    public async Task<bool> TryIncrementAsync(string userId, int maxConcurrent, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        Container container = await GetContainerAsync(ct);
        string docId = $"counter-{userId}";

        // Retry loop for optimistic concurrency
        for (int attempt = 0; attempt < 5; attempt++)
        {
            ConcurrencyCounter counter;
            string? etag = null;

            try
            {
                ItemResponse<ConcurrencyCounter> response = await container.ReadItemAsync<ConcurrencyCounter>(
                    docId, new PartitionKey(userId), cancellationToken: ct);
                counter = response.Resource;
                etag = response.ETag;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                counter = new ConcurrencyCounter
                {
                    Id = docId,
                    UserId = userId,
                    ActiveCount = 0
                };
            }

            if (counter.ActiveCount >= maxConcurrent)
                return false;

            ConcurrencyCounter updated = counter with { ActiveCount = counter.ActiveCount + 1 };

            try
            {
                ItemRequestOptions options = new();
                if (etag is not null)
                {
                    options.IfMatchEtag = etag;
                }

                await container.UpsertItemAsync(updated, new PartitionKey(userId),
                    requestOptions: options, cancellationToken: ct);
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // Conflict — retry with fresh read
                _logger.LogDebug("Concurrency conflict for user {UserId}, retrying (attempt {Attempt})", userId, attempt + 1);
            }
        }

        _logger.LogWarning("Failed to increment counter for user {UserId} after retries", userId);
        return false;
    }

    public async Task DecrementAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        Container container = await GetContainerAsync(ct);
        string docId = $"counter-{userId}";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                ItemResponse<ConcurrencyCounter> response = await container.ReadItemAsync<ConcurrencyCounter>(
                    docId, new PartitionKey(userId), cancellationToken: ct);

                ConcurrencyCounter updated = response.Resource with
                {
                    ActiveCount = Math.Max(0, response.Resource.ActiveCount - 1)
                };

                await container.UpsertItemAsync(updated, new PartitionKey(userId),
                    requestOptions: new ItemRequestOptions { IfMatchEtag = response.ETag },
                    cancellationToken: ct);
                return;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Nothing to decrement
                return;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                _logger.LogDebug("Concurrency conflict decrementing for user {UserId}, retrying (attempt {Attempt})", userId, attempt + 1);
            }
        }
    }

    private async Task<Container> GetContainerAsync(CancellationToken ct)
    {
        if (_container is not null)
            return _container;

        Database database = await _client.CreateDatabaseIfNotExistsAsync(_databaseName, cancellationToken: ct);
        ContainerProperties properties = new(_containerName, "/userId");
        ContainerResponse containerResponse = await database.CreateContainerIfNotExistsAsync(properties, cancellationToken: ct);
        _container = containerResponse.Container;

        _logger.LogInformation("Cosmos concurrency counter initialized: {Database}/{Container}", _databaseName, _containerName);
        return _container;
    }
}
