using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Interfaces;

public interface IAgentRegistry
{
    Task<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken ct = default);
    Task<AgentDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task<AgentDefinition> CreateAsync(AgentDefinition definition, CancellationToken ct = default);
    Task<AgentDefinition> UpdateAsync(AgentDefinition definition, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<bool> IsBuiltInAsync(string id, CancellationToken ct = default);
}
