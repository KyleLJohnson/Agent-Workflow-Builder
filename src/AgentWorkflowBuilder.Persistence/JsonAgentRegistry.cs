using System.Text.Json;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Persistence;

public class JsonAgentRegistry : IAgentRegistry
{
    private readonly string _builtInDir;
    private readonly string _customDir;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonAgentRegistry(string dataBasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataBasePath);

        _builtInDir = Path.Combine(dataBasePath, "agents", "builtin");
        _customDir = Path.Combine(dataBasePath, "agents", "custom");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);
    }

    public async Task<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken ct = default)
    {
        var agents = new List<AgentDefinition>();
        agents.AddRange(await LoadFromDirectoryAsync(_builtInDir, ct));
        agents.AddRange(await LoadFromDirectoryAsync(_customDir, ct));
        return agents.AsReadOnly();
    }

    public async Task<AgentDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        // Check built-in first, then custom
        var builtInPath = Path.Combine(_builtInDir, $"{id}.json");
        if (File.Exists(builtInPath))
            return await ReadFileAsync(builtInPath, ct);

        var customPath = Path.Combine(_customDir, $"{id}.json");
        if (File.Exists(customPath))
            return await ReadFileAsync(customPath, ct);

        return null;
    }

    public async Task<AgentDefinition> CreateAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var agent = definition with
        {
            Id = Guid.NewGuid().ToString(),
            IsBuiltIn = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var path = Path.Combine(_customDir, $"{agent.Id}.json");
        await WriteFileAsync(path, agent, ct);
        return agent;
    }

    public async Task<AgentDefinition> UpdateAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (await IsBuiltInAsync(definition.Id, ct))
            throw new InvalidOperationException("Cannot modify a built-in agent.");

        var path = Path.Combine(_customDir, $"{definition.Id}.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Custom agent '{definition.Id}' not found.");

        var updated = definition with { UpdatedAt = DateTime.UtcNow };
        await WriteFileAsync(path, updated, ct);
        return updated;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (await IsBuiltInAsync(id, ct))
            throw new InvalidOperationException("Cannot delete a built-in agent.");

        var path = Path.Combine(_customDir, $"{id}.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Custom agent '{id}' not found.");

        File.Delete(path);
    }

    public Task<bool> IsBuiltInAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_builtInDir, $"{id}.json");
        return Task.FromResult(File.Exists(path));
    }

    private async Task<List<AgentDefinition>> LoadFromDirectoryAsync(string dir, CancellationToken ct)
    {
        var agents = new List<AgentDefinition>();
        if (!Directory.Exists(dir)) return agents;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var agent = await ReadFileAsync(file, ct);
            if (agent != null) agents.Add(agent);
        }

        return agents;
    }

    private static async Task<AgentDefinition?> ReadFileAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<AgentDefinition>(json, JsonOptions);
    }

    private static async Task WriteFileAsync(string path, AgentDefinition definition, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(definition, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }
}
