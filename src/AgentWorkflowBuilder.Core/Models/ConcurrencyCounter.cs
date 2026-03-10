using System.Text.Json.Serialization;

namespace AgentWorkflowBuilder.Core.Models;

/// <summary>
/// Cosmos DB document for tracking per-user concurrent execution counts.
/// Partition key: /userId
/// </summary>
public record ConcurrencyCounter
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("activeCount")]
    public int ActiveCount { get; init; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}
