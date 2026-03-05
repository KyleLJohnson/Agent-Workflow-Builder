using AgentWorkflowBuilder.Core.Models;
using GitHub.Copilot.SDK;

namespace AgentWorkflowBuilder.Core.Interfaces;

/// <summary>
/// Creates configured <see cref="CopilotSession"/> instances for agent execution.
/// </summary>
public interface ICopilotSessionFactory
{
    /// <summary>
    /// Creates a new Copilot session configured for the given agent definition.
    /// </summary>
    /// <param name="definition">The agent definition with system instructions, model, and MCP config.</param>
    /// <param name="onEvent">Optional callback invoked for streaming events (e.g., delta chunks).</param>
    /// <param name="onUserInput">Optional callback for handling user input requests (clarification).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CopilotSession> CreateSessionAsync(
        AgentDefinition definition,
        Action<WorkflowExecutionEvent>? onEvent = null,
        Func<UserInputRequest, UserInputInvocation, Task<UserInputResponse>>? onUserInput = null,
        CancellationToken ct = default);
}
