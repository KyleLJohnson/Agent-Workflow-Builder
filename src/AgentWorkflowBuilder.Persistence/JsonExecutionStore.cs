using System.Text.Json;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Persistence;

/// <summary>
/// JSON file-based implementation of <see cref="IExecutionStore"/> for local development.
/// </summary>
public class JsonExecutionStore : IExecutionStore
{
    private readonly string _executionsDir;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JsonExecutionStore(string dataBasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataBasePath);
        _executionsDir = Path.Combine(dataBasePath, "executions");
        Directory.CreateDirectory(_executionsDir);
    }

    public async Task SaveAsync(ExecutionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        string path = GetRecordPath(record.Id);
        string json = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public async Task<ExecutionRecord?> GetAsync(string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        string path = GetRecordPath(executionId);
        if (!File.Exists(path))
            return null;

        string json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ExecutionRecord>(json);
    }

    public async Task<IReadOnlyList<ExecutionRecord>> ListByWorkflowAsync(string workflowId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        List<ExecutionRecord> records = [];

        if (!Directory.Exists(_executionsDir))
            return records;

        foreach (string file in Directory.GetFiles(_executionsDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            string json = await File.ReadAllTextAsync(file, ct);
            ExecutionRecord? record = JsonSerializer.Deserialize<ExecutionRecord>(json);
            if (record is not null && record.WorkflowId == workflowId)
                records.Add(record);
        }

        return records;
    }

    public async Task<IReadOnlyList<ExecutionRecord>> GetPausedAsync(CancellationToken ct = default)
    {
        List<ExecutionRecord> paused = [];

        if (!Directory.Exists(_executionsDir))
            return paused;

        foreach (string file in Directory.GetFiles(_executionsDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            string json = await File.ReadAllTextAsync(file, ct);
            ExecutionRecord? record = JsonSerializer.Deserialize<ExecutionRecord>(json);
            if (record is not null && record.Status == ExecutionStatus.Paused)
                paused.Add(record);
        }

        return paused;
    }

    private string GetRecordPath(string executionId)
    {
        return Path.Combine(_executionsDir, $"{executionId}.json");
    }
}
