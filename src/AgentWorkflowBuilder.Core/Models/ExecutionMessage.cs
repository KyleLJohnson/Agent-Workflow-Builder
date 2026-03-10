using System.Text.Json.Serialization;

namespace AgentWorkflowBuilder.Core.Models;

/// <summary>
/// Message enqueued to Service Bus to request workflow execution.
/// </summary>
public record ExecutionMessage
{
    [JsonPropertyName("executionId")]
    public string ExecutionId { get; init; } = string.Empty;

    [JsonPropertyName("workflowId")]
    public string WorkflowId { get; init; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; init; } = string.Empty;

    [JsonPropertyName("inputMessage")]
    public string InputMessage { get; init; } = string.Empty;

    [JsonPropertyName("autoApproveGates")]
    public bool AutoApproveGates { get; init; }

    [JsonPropertyName("enqueuedAt")]
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
}
