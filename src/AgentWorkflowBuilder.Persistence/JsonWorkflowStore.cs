using System.Text.Json;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Persistence;

public class JsonWorkflowStore : IWorkflowStore
{
    private readonly string _workflowDir;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonWorkflowStore(string dataBasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataBasePath);

        _workflowDir = Path.Combine(dataBasePath, "workflows");
        Directory.CreateDirectory(_workflowDir);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(string? userId = null, CancellationToken ct = default)
    {
        List<WorkflowDefinition> workflows = [];
        if (!Directory.Exists(_workflowDir)) return workflows.AsReadOnly();

        foreach (string file in Directory.GetFiles(_workflowDir, "*.json"))
        {
            WorkflowDefinition? workflow = await ReadFileAsync(file, ct);
            if (workflow is null) continue;
            if (userId is not null && workflow.UserId != userId) continue;
            workflows.Add(workflow);
        }

        return workflows.AsReadOnly();
    }

    public async Task<WorkflowDefinition?> GetAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_workflowDir, $"{id}.json");
        if (!File.Exists(path)) return null;
        return await ReadFileAsync(path, ct);
    }

    public async Task<WorkflowDefinition> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var workflow = definition with
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var path = Path.Combine(_workflowDir, $"{workflow.Id}.json");
        await WriteFileAsync(path, workflow, ct);
        return workflow;
    }

    public async Task<WorkflowDefinition> UpdateAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var path = Path.Combine(_workflowDir, $"{definition.Id}.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Workflow '{definition.Id}' not found.");

        var updated = definition with { UpdatedAt = DateTime.UtcNow };
        await WriteFileAsync(path, updated, ct);
        return updated;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var path = Path.Combine(_workflowDir, $"{id}.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Workflow '{id}' not found.");

        File.Delete(path);
        return Task.CompletedTask;
    }

    private static async Task<WorkflowDefinition?> ReadFileAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions);
    }

    private static async Task WriteFileAsync(string path, WorkflowDefinition definition, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(definition, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }
}
