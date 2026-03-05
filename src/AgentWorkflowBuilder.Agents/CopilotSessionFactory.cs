using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace AgentWorkflowBuilder.Agents;

/// <summary>
/// Creates configured <see cref="CopilotSession"/> instances from <see cref="AgentDefinition"/>.
/// </summary>
public class CopilotSessionFactory : ICopilotSessionFactory
{
    private readonly CopilotClient _client;
    private readonly CopilotProviderFactory _providerFactory;
    private readonly IMcpConfigStore _mcpConfigStore;
    private readonly ILogger<CopilotSessionFactory> _logger;

    public CopilotSessionFactory(
        CopilotClient client,
        CopilotProviderFactory providerFactory,
        IMcpConfigStore mcpConfigStore,
        ILogger<CopilotSessionFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(providerFactory);
        ArgumentNullException.ThrowIfNull(mcpConfigStore);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _providerFactory = providerFactory;
        _mcpConfigStore = mcpConfigStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CopilotSession> CreateSessionAsync(
        AgentDefinition definition,
        Action<WorkflowExecutionEvent>? onEvent = null,
        Func<UserInputRequest, UserInputInvocation, Task<UserInputResponse>>? onUserInput = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        string model = definition.ModelOverride ?? _providerFactory.DefaultModel;
        ProviderConfig provider = _providerFactory.CreateProviderConfig();

        SessionConfig config = new()
        {
            Model = model,
            Provider = provider,
            SystemMessage = new SystemMessageConfig
            {
                Content = definition.SystemInstructions ?? string.Empty,
                Mode = SystemMessageMode.Replace
            },
            Streaming = true
        };

        // Map MCP servers from agent definition
        Dictionary<string, object>? mcpServers = await BuildMcpServersAsync(definition, ct);
        if (mcpServers is not null && mcpServers.Count > 0)
        {
            config.McpServers = mcpServers;
        }

        // Wire user input callback for clarification questions
        if (onUserInput is not null)
        {
            config.OnUserInputRequest = async (request, invocation) =>
                await onUserInput(request, invocation);
        }

        CopilotSession session = await _client.CreateSessionAsync(config);

        // Subscribe to streaming delta events if caller wants them
        if (onEvent is not null)
        {
            session.On(evt =>
            {
                if (evt is AssistantMessageDeltaEvent delta)
                {
                    string? chunk = delta.Data?.DeltaContent;
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        onEvent(new WorkflowExecutionEvent
                        {
                            EventType = ExecutionEventType.WorkflowOutput,
                            ExecutorName = definition.Name,
                            Data = chunk
                        });
                    }
                }
            });
        }

        _logger.LogDebug("Created Copilot session for agent '{AgentName}' with model '{Model}'",
            definition.Name, model);

        return session;
    }

    /// <summary>
    /// Maps <see cref="AgentDefinition.McpServerIds"/> to SDK MCP server config objects.
    /// </summary>
    private async Task<Dictionary<string, object>?> BuildMcpServersAsync(
        AgentDefinition definition,
        CancellationToken ct)
    {
        if (definition.McpServerIds is null || definition.McpServerIds.Count == 0)
            return null;

        Dictionary<string, object> servers = new(StringComparer.Ordinal);

        foreach (string serverId in definition.McpServerIds)
        {
            McpServerConfig? serverConfig = await _mcpConfigStore.GetServerAsync(serverId, ct);
            if (serverConfig is null)
            {
                _logger.LogWarning("MCP server '{ServerId}' referenced by agent '{AgentName}' not found, skipping",
                    serverId, definition.Name);
                continue;
            }

            if (!serverConfig.Enabled)
            {
                _logger.LogDebug("MCP server '{ServerName}' is disabled, skipping", serverConfig.Name);
                continue;
            }

            object sdkServer = MapToSdkServer(serverConfig);
            servers[serverConfig.Name] = sdkServer;
        }

        return servers;
    }

    /// <summary>
    /// Converts our <see cref="McpServerConfig"/> to the SDK's anonymous MCP server config format.
    /// </summary>
    private static object MapToSdkServer(McpServerConfig config)
    {
        if (config.TransportType.Equals("stdio", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                type = config.TransportType,
                command = config.Command,
                args = config.Args?.ToArray(),
                env = config.Env
            };
        }

        // SSE/HTTP transport
        return new
        {
            type = config.TransportType,
            url = config.Url
        };
    }
}
