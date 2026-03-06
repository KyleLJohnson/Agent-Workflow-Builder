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

    [JsonPropertyName("executionId")]
    public string? ExecutionId { get; init; }

    [JsonPropertyName("nodeId")]
    public string? NodeId { get; init; }

    [JsonPropertyName("executorName")]
    public string? ExecutorName { get; init; }

    [JsonPropertyName("data")]
    public string Data { get; init; } = string.Empty;

    [JsonPropertyName("question")]
    public string? Question { get; init; }

    [JsonPropertyName("previousAgentOutput")]
    public string? PreviousAgentOutput { get; init; }

    [JsonPropertyName("gateType")]
    public string? GateType { get; init; }

    [JsonPropertyName("gateInstructions")]
    public string? GateInstructions { get; init; }

    [JsonPropertyName("planSteps")]
    public List<PlanStepInfo>? PlanSteps { get; init; }

    [JsonPropertyName("loopIteration")]
    public int? LoopIteration { get; init; }

    [JsonPropertyName("maxIterations")]
    public int? MaxIterations { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public enum ExecutionEventType
{
    AgentStepStarted,
    AgentStepCompleted,
    WorkflowOutput,
    Error,
    ClarificationNeeded,
    GateAwaitingApproval,
    GateApproved,
    GateRejected,
    LoopIterationStarted,
    LoopIterationCompleted,
    PlanGenerated,
    PlanTriggered
}

/// <summary>
/// A step in a planner-generated execution plan.
/// </summary>
public record PlanStepInfo
{
    [JsonPropertyName("stepNumber")]
    public int StepNumber { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("instruction")]
    public string Instruction { get; init; } = string.Empty;

    [JsonPropertyName("agentHint")]
    public string AgentHint { get; init; } = string.Empty;

    [JsonPropertyName("matchedAgentId")]
    public string? MatchedAgentId { get; init; }

    [JsonPropertyName("matchedAgentName")]
    public string? MatchedAgentName { get; init; }
}

/// <summary>
/// The response from a gate node interaction.
/// </summary>
public record GateResponse
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GateResponseStatus Status { get; init; }

    public string? EditedOutput { get; init; }
    public string? Reason { get; init; }
    public string? Feedback { get; init; }
}

public enum GateResponseStatus
{
    Approved,
    Rejected,
    SendBack
}

/// <summary>
/// Persisted record of a workflow execution for history and recovery.
/// </summary>
public record ExecutionRecord
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("workflowId")]
    public string WorkflowId { get; init; } = string.Empty;

    [JsonPropertyName("triggerType")]
    public string TriggerType { get; init; } = "manual";

    [JsonPropertyName("blobName")]
    public string? BlobName { get; init; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExecutionStatus Status { get; init; }

    [JsonPropertyName("currentNodeId")]
    public string? CurrentNodeId { get; init; }

    [JsonPropertyName("agentOutputs")]
    public Dictionary<string, string> AgentOutputs { get; init; } = new();

    [JsonPropertyName("accumulatedContext")]
    public string? AccumulatedContext { get; init; }

    [JsonPropertyName("pauseType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PauseType PauseType { get; init; }

    [JsonPropertyName("pauseDetails")]
    public string? PauseDetails { get; init; }

    [JsonPropertyName("events")]
    public List<WorkflowExecutionEvent> Events { get; init; } = [];

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; init; }
}

public enum ExecutionStatus
{
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum PauseType
{
    None,
    Gate,
    Clarification,
    SendBack
}
