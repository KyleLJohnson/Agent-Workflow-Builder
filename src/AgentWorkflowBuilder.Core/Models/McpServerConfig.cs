using System.Text.Json.Serialization;

namespace AgentWorkflowBuilder.Core.Models;

/// <summary>
/// Configuration for connecting to an external MCP server.
/// </summary>
public record McpServerConfig
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Transport type: "stdio" or "sse".
    /// </summary>
    [JsonPropertyName("transportType")]
    public string TransportType { get; init; } = "stdio";

    /// <summary>
    /// For stdio transport: the command to launch the MCP server process.
    /// </summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>
    /// For stdio transport: arguments passed to the command.
    /// </summary>
    [JsonPropertyName("args")]
    public List<string>? Args { get; init; }

    /// <summary>
    /// For stdio transport: environment variables to set on the child process.
    /// </summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    /// <summary>
    /// For SSE transport: the URL of the MCP server.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Root configuration object stored in mcp.json.
/// </summary>
public record McpConfiguration
{
    [JsonPropertyName("servers")]
    public List<McpServerConfig> Servers { get; init; } = [];
}

/// <summary>
/// Describes an MCP tool discovered from a connected server.
/// </summary>
public record McpToolInfo
{
    [JsonPropertyName("serverId")]
    public string ServerId { get; init; } = string.Empty;

    [JsonPropertyName("serverName")]
    public string ServerName { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("inputSchema")]
    public object? InputSchema { get; init; }
}

/// <summary>
/// Request to invoke an MCP tool.
/// </summary>
public record McpToolCallRequest
{
    [JsonPropertyName("serverId")]
    public string ServerId { get; init; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string ToolName { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?>? Arguments { get; init; }
}

/// <summary>
/// Response from invoking an MCP tool.
/// </summary>
public record McpToolCallResponse
{
    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; init; }

    [JsonPropertyName("content")]
    public List<McpToolContent> Content { get; init; } = [];

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

public record McpToolContent
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
