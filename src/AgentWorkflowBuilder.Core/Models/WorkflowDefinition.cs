using System.Text.Json.Serialization;

namespace AgentWorkflowBuilder.Core.Models;

/// <summary>
/// Represents a workflow composed of agent nodes connected by edges.
/// </summary>
public record WorkflowDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; init; } = "New Workflow";

    [JsonPropertyName("nodes")]
    public List<WorkflowNode> Nodes { get; init; } = [];

    [JsonPropertyName("edges")]
    public List<WorkflowEdge> Edges { get; init; } = [];

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    [JsonPropertyName("blobContainerName")]
    public string? BlobContainerName { get; init; }
}

/// <summary>
/// A single node in a workflow graph, bound to a specific agent.
/// </summary>
public record WorkflowNode
{
    [JsonPropertyName("nodeId")]
    public string NodeId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("agentId")]
    public string AgentId { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("positionX")]
    public double PositionX { get; init; }

    [JsonPropertyName("positionY")]
    public double PositionY { get; init; }

    [JsonPropertyName("nodeType")]
    public string NodeType { get; init; } = "agent";

    [JsonPropertyName("gateConfig")]
    public GateConfiguration? GateConfig { get; init; }

    [JsonPropertyName("configOverrides")]
    public Dictionary<string, string>? ConfigOverrides { get; init; }
}

/// <summary>
/// Configuration for a gate node (approval or review-and-edit).
/// </summary>
public record GateConfiguration
{
    [JsonPropertyName("gateType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GateType GateType { get; init; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    [JsonPropertyName("sendBackTargetNodeId")]
    public string? SendBackTargetNodeId { get; init; }
}

public enum GateType
{
    Approval,
    ReviewAndEdit
}

/// <summary>
/// A directed edge connecting two workflow nodes.
/// </summary>
public record WorkflowEdge
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("sourceNodeId")]
    public string SourceNodeId { get; init; } = string.Empty;

    [JsonPropertyName("targetNodeId")]
    public string TargetNodeId { get; init; } = string.Empty;

    [JsonPropertyName("isBackEdge")]
    public bool IsBackEdge { get; init; }

    [JsonPropertyName("maxIterations")]
    public int? MaxIterations { get; init; }
}
