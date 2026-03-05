using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Interfaces;

/// <summary>
/// Provides CRUD access to MCP server configurations (config-only, no connections).
/// </summary>
public interface IMcpConfigStore
{
    /// <summary>
    /// Returns all configured MCP servers.
    /// </summary>
    Task<IReadOnlyList<McpServerConfig>> ListServersAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a single MCP server configuration by ID, or null if not found.
    /// </summary>
    Task<McpServerConfig?> GetServerAsync(string serverId, CancellationToken ct = default);

    /// <summary>
    /// Adds a new MCP server configuration.
    /// </summary>
    Task<McpServerConfig> AddServerAsync(McpServerConfig config, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing MCP server configuration.
    /// </summary>
    Task<McpServerConfig> UpdateServerAsync(McpServerConfig config, CancellationToken ct = default);

    /// <summary>
    /// Removes an MCP server configuration by ID.
    /// </summary>
    Task RemoveServerAsync(string serverId, CancellationToken ct = default);
}
