using System.Text.Json.Serialization;

namespace AgentWorkflowBuilder.Core.Models;

/// <summary>
/// Represents the definition of an AI agent with its configuration and metadata.
/// </summary>
public record AgentDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("systemInstructions")]
    public string SystemInstructions { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = "Custom";

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "🤖";

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; init; }

    [JsonPropertyName("inputDescription")]
    public string InputDescription { get; init; } = "Text input";

    [JsonPropertyName("outputDescription")]
    public string OutputDescription { get; init; } = "Text output";

    [JsonPropertyName("modelOverride")]
    public string? ModelOverride { get; init; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }

    [JsonPropertyName("mcpServerIds")]
    public List<string> McpServerIds { get; init; } = [];

    [JsonPropertyName("allowClarification")]
    public bool AllowClarification { get; init; } = true;

    [JsonPropertyName("agentType")]
    public string AgentType { get; init; } = "standard";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
