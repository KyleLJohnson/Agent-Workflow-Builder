using System.Text.Json;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AgentWorkflowBuilder.Persistence;

/// <summary>
/// Manages MCP server configurations (persisted to mcp.json) and provides
/// on-demand tool discovery/invocation for the settings panel.
/// </summary>
public class McpClientManager : IMcpConfigStore, IMcpClientManager
{
    private readonly string _configPath;
    private McpConfiguration _config = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public McpClientManager(string dataBasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataBasePath);
        _configPath = Path.Combine(dataBasePath, "mcp.json");
    }

    // ------------------------------------------------------------------
    // Lifecycle — loads config only (no persistent connections)
    // ------------------------------------------------------------------

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _config = await LoadConfigAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    // ------------------------------------------------------------------
    // IMcpConfigStore — Server CRUD
    // ------------------------------------------------------------------

    public Task<IReadOnlyList<McpServerConfig>> ListServersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<McpServerConfig>>(_config.Servers.AsReadOnly());

    public Task<McpServerConfig?> GetServerAsync(string serverId, CancellationToken ct = default)
    {
        McpServerConfig? server = _config.Servers.FirstOrDefault(s => s.Id == serverId);
        return Task.FromResult(server);
    }

    public async Task<McpServerConfig> AddServerAsync(McpServerConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        McpServerConfig server = config with
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _lock.WaitAsync(ct);
        try
        {
            _config = _config with { Servers = [.. _config.Servers, server] };
            await SaveConfigAsync(ct);
        }
        finally { _lock.Release(); }

        return server;
    }

    public async Task<McpServerConfig> UpdateServerAsync(McpServerConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _lock.WaitAsync(ct);
        try
        {
            int index = _config.Servers.FindIndex(s => s.Id == config.Id);
            if (index < 0)
                throw new FileNotFoundException($"MCP server '{config.Id}' not found.");

            McpServerConfig updated = config with { UpdatedAt = DateTime.UtcNow };
            List<McpServerConfig> list = _config.Servers.ToList();
            list[index] = updated;
            _config = _config with { Servers = list };
            await SaveConfigAsync(ct);

            return updated;
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveServerAsync(string serverId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            int index = _config.Servers.FindIndex(s => s.Id == serverId);
            if (index < 0)
                throw new FileNotFoundException($"MCP server '{serverId}' not found.");

            List<McpServerConfig> list = _config.Servers.ToList();
            list.RemoveAt(index);
            _config = _config with { Servers = list };
            await SaveConfigAsync(ct);
        }
        finally { _lock.Release(); }
    }

    // ------------------------------------------------------------------
    // IMcpClientManager — On-demand tool discovery & invocation
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct = default)
    {
        List<McpToolInfo> allTools = [];

        foreach (McpServerConfig serverConfig in _config.Servers.Where(s => s.Enabled))
        {
            try
            {
                await using McpClient client = await CreateTemporaryClientAsync(serverConfig, ct);
                IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: ct);
                foreach (McpClientTool tool in tools)
                {
                    allTools.Add(new McpToolInfo
                    {
                        ServerId = serverConfig.Id,
                        ServerName = serverConfig.Name,
                        Name = tool.Name,
                        Description = tool.Description,
                        InputSchema = tool.JsonSchema.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARNING: Failed to list tools from MCP server '{serverConfig.Name}': {ex.Message}");
            }
        }

        return allTools.AsReadOnly();
    }

    public async Task<McpToolCallResponse> CallToolAsync(McpToolCallRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        McpServerConfig? serverConfig = _config.Servers.FirstOrDefault(s => s.Id == request.ServerId);
        if (serverConfig is null)
        {
            return new McpToolCallResponse
            {
                IsSuccess = false,
                Error = $"MCP server '{request.ServerId}' not found."
            };
        }

        try
        {
            await using McpClient client = await CreateTemporaryClientAsync(serverConfig, ct);

            IReadOnlyDictionary<string, object?>? args = request.Arguments?
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                as IReadOnlyDictionary<string, object?>;

            var result = await client.CallToolAsync(request.ToolName, args, cancellationToken: ct);

            List<McpToolContent> content = result.Content
                .OfType<TextContentBlock>()
                .Select(c => new McpToolContent
                {
                    Type = "text",
                    Text = c.Text
                })
                .ToList();

            return new McpToolCallResponse
            {
                IsSuccess = !(result.IsError ?? false),
                Content = content,
                Error = (result.IsError ?? false) ? string.Join("\n", content.Select(c => c.Text)) : null
            };
        }
        catch (Exception ex)
        {
            return new McpToolCallResponse
            {
                IsSuccess = false,
                Error = ex.Message
            };
        }
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a temporary MCP client connection for on-demand tool operations.
    /// Caller is responsible for disposing the client.
    /// </summary>
    private static async Task<McpClient> CreateTemporaryClientAsync(McpServerConfig server, CancellationToken ct)
    {
        McpClientOptions clientOptions = new()
        {
            ClientInfo = new() { Name = "AgentWorkflowBuilder", Version = "1.0.0" }
        };

        if (server.TransportType.Equals("sse", StringComparison.OrdinalIgnoreCase)
            || server.TransportType.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(server.Url))
                throw new InvalidOperationException($"MCP server '{server.Name}' uses SSE/HTTP transport but has no URL.");

            HttpClientTransport transport = new(new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.Url)
            });

            return await McpClient.CreateAsync(transport, clientOptions, cancellationToken: ct);
        }

        // stdio transport
        if (string.IsNullOrWhiteSpace(server.Command))
            throw new InvalidOperationException($"MCP server '{server.Name}' uses stdio transport but has no command.");

        StdioClientTransportOptions transportOptions = new()
        {
            Command = server.Command,
            Arguments = server.Args ?? []
        };

        if (server.Env is { Count: > 0 })
        {
            foreach ((string key, string value) in server.Env)
            {
                transportOptions.EnvironmentVariables![key] = value;
            }
        }

        StdioClientTransport stdioTransport = new(transportOptions);
        return await McpClient.CreateAsync(stdioTransport, clientOptions, cancellationToken: ct);
    }

    private async Task<McpConfiguration> LoadConfigAsync(CancellationToken ct)
    {
        if (!File.Exists(_configPath))
        {
            McpConfiguration empty = new();
            await SaveConfigInternalAsync(empty, ct);
            return empty;
        }

        string json = await File.ReadAllTextAsync(_configPath, ct);
        return JsonSerializer.Deserialize<McpConfiguration>(json, JsonOptions) ?? new McpConfiguration();
    }

    private Task SaveConfigAsync(CancellationToken ct) => SaveConfigInternalAsync(_config, ct);

    private async Task SaveConfigInternalAsync(McpConfiguration config, CancellationToken ct)
    {
        string? dir = Path.GetDirectoryName(_configPath);
        if (dir is not null) Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(_configPath, json, ct);
    }
}
