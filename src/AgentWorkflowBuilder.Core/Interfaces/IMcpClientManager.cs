using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Interfaces;

/// <summary>
/// Manages connections to external MCP servers and exposes their tools.
/// </summary>
public interface IMcpClientManager : IAsyncDisposable
{
    /// <summary>
    /// Loads server configurations and connects to all enabled servers.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all configured MCP servers.
    /// </summary>
    Task<IReadOnlyList<McpServerConfig>> ListServersAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a new MCP server configuration and connects to it.
    /// </summary>
    Task<McpServerConfig> AddServerAsync(McpServerConfig config, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing MCP server configuration, reconnecting if needed.
    /// </summary>
    Task<McpServerConfig> UpdateServerAsync(McpServerConfig config, CancellationToken ct = default);

    /// <summary>
    /// Removes an MCP server configuration and disconnects.
    /// </summary>
    Task RemoveServerAsync(string serverId, CancellationToken ct = default);

    /// <summary>
    /// Lists all tools available across all connected MCP servers.
    /// </summary>
    Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct = default);

    /// <summary>
    /// Invokes a tool on a specific MCP server.
    /// </summary>
    Task<McpToolCallResponse> CallToolAsync(McpToolCallRequest request, CancellationToken ct = default);
}
