using System.Text.Json.Serialization;

namespace AgentWorkflowBuilder.Core.Models;

/// <summary>
/// Request payload for executing a workflow.
/// </summary>
public record WorkflowExecutionRequest
{
    [JsonPropertyName("workflowId")]
    public string WorkflowId { get; init; } = string.Empty;

    [JsonPropertyName("inputType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExecutionInputType InputType { get; init; } = ExecutionInputType.Chat;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("parameters")]
    public Dictionary<string, string>? Parameters { get; init; }
}

public enum ExecutionInputType
{
    Chat,
    File
}

/// <summary>
/// An event emitted during workflow execution (step progress, output, or error).
/// </summary>
public record WorkflowExecutionEvent
{
    [JsonPropertyName("eventType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExecutionEventType EventType { get; init; }

    [JsonPropertyName("nodeId")]
    public string? NodeId { get; init; }

    [JsonPropertyName("executorName")]
    public string? ExecutorName { get; init; }

    [JsonPropertyName("data")]
    public string Data { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public enum ExecutionEventType
{
    AgentStepStarted,
    AgentStepCompleted,
    WorkflowOutput,
    Error
}
