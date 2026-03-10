using System.Text.Json.Serialization;

namespace AgentWorkflowBuilder.Core.Models;

/// <summary>
/// Cosmos DB document used for distributed session signaling (gate responses  and clarification answers).
/// Change feed listeners watch this container to unblock waiting workers.
/// Partition key: /executionId
/// </summary>
public record SessionDocument
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("executionId")]
    public string ExecutionId { get; init; } = string.Empty;

    [JsonPropertyName("documentType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionDocumentType DocumentType { get; init; }

    [JsonPropertyName("clarificationAnswer")]
    public string? ClarificationAnswer { get; init; }

    [JsonPropertyName("gateResponse")]
    public GateResponse? GateResponse { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// TTL in seconds — session docs auto-expire after 24 hours.
    /// </summary>
    [JsonPropertyName("ttl")]
    public int Ttl { get; init; } = 86400;
}

public enum SessionDocumentType
{
    ClarificationResponse,
    GateResponse
}
