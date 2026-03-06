using AgentWorkflowBuilder.Core.Models;
using AgentWorkflowBuilder.Persistence;

namespace AgentWorkflowBuilder.Persistence.Tests;

public class JsonExecutionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonExecutionStore _store;

    public JsonExecutionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "awb-exec-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new JsonExecutionStore(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public async Task WhenSaveRecordThenPersistsToDisk()
    {
        ExecutionRecord record = new()
        {
            Id = "exec-1",
            WorkflowId = "wf-1",
            Status = ExecutionStatus.Running
        };

        await _store.SaveAsync(record);

        ExecutionRecord? retrieved = await _store.GetAsync("exec-1");
        Assert.NotNull(retrieved);
        Assert.Equal("exec-1", retrieved.Id);
        Assert.Equal("wf-1", retrieved.WorkflowId);
        Assert.Equal(ExecutionStatus.Running, retrieved.Status);
    }

    [Fact]
    public async Task WhenSaveExistingRecordThenOverwrites()
    {
        ExecutionRecord original = new()
        {
            Id = "exec-1",
            WorkflowId = "wf-1",
            Status = ExecutionStatus.Running
        };
        await _store.SaveAsync(original);

        ExecutionRecord updated = original with
        {
            Status = ExecutionStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };
        await _store.SaveAsync(updated);

        ExecutionRecord? retrieved = await _store.GetAsync("exec-1");
        Assert.NotNull(retrieved);
        Assert.Equal(ExecutionStatus.Completed, retrieved.Status);
        Assert.NotNull(retrieved.CompletedAt);
    }

    [Fact]
    public async Task WhenGetNonexistentThenReturnsNull()
    {
        ExecutionRecord? result = await _store.GetAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task WhenListByWorkflowThenFiltersCorrectly()
    {
        await _store.SaveAsync(new ExecutionRecord { Id = "e1", WorkflowId = "wf-1" });
        await _store.SaveAsync(new ExecutionRecord { Id = "e2", WorkflowId = "wf-1" });
        await _store.SaveAsync(new ExecutionRecord { Id = "e3", WorkflowId = "wf-2" });

        IReadOnlyList<ExecutionRecord> results = await _store.ListByWorkflowAsync("wf-1");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("wf-1", r.WorkflowId));
    }

    [Fact]
    public async Task WhenListByWorkflowNoMatchThenReturnsEmpty()
    {
        await _store.SaveAsync(new ExecutionRecord { Id = "e1", WorkflowId = "wf-1" });

        IReadOnlyList<ExecutionRecord> results = await _store.ListByWorkflowAsync("wf-999");

        Assert.Empty(results);
    }

    [Fact]
    public async Task WhenGetPausedThenReturnsOnlyPausedRecords()
    {
        await _store.SaveAsync(new ExecutionRecord
        {
            Id = "e1", WorkflowId = "wf-1",
            Status = ExecutionStatus.Paused,
            PauseType = PauseType.Clarification
        });
        await _store.SaveAsync(new ExecutionRecord
        {
            Id = "e2", WorkflowId = "wf-1",
            Status = ExecutionStatus.Running
        });
        await _store.SaveAsync(new ExecutionRecord
        {
            Id = "e3", WorkflowId = "wf-2",
            Status = ExecutionStatus.Paused,
            PauseType = PauseType.Gate
        });

        IReadOnlyList<ExecutionRecord> paused = await _store.GetPausedAsync();

        Assert.Equal(2, paused.Count);
        Assert.All(paused, r => Assert.Equal(ExecutionStatus.Paused, r.Status));
    }

    [Fact]
    public async Task WhenNoPausedRecordsThenReturnsEmpty()
    {
        await _store.SaveAsync(new ExecutionRecord { Id = "e1", Status = ExecutionStatus.Running });
        await _store.SaveAsync(new ExecutionRecord { Id = "e2", Status = ExecutionStatus.Completed });

        IReadOnlyList<ExecutionRecord> paused = await _store.GetPausedAsync();

        Assert.Empty(paused);
    }

    [Fact]
    public void WhenNullDataBasePathThenThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new JsonExecutionStore(null!));
    }

    [Fact]
    public void WhenWhitespaceDataBasePathThenThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new JsonExecutionStore("  "));
    }

    [Fact]
    public async Task WhenSaveNullRecordThenThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _store.SaveAsync(null!));
    }

    [Fact]
    public async Task WhenGetNullIdThenThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _store.GetAsync(null!));
    }

    [Fact]
    public async Task WhenListByWorkflowNullIdThenThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _store.ListByWorkflowAsync(null!));
    }

    [Fact]
    public async Task WhenRecordWithAgentOutputsThenRoundTrips()
    {
        ExecutionRecord record = new()
        {
            Id = "exec-outputs",
            WorkflowId = "wf-1",
            AgentOutputs = new Dictionary<string, string>
            {
                ["node-1"] = "First agent response",
                ["node-2"] = "Second agent response"
            },
            AccumulatedContext = "Full context here"
        };

        await _store.SaveAsync(record);
        ExecutionRecord? retrieved = await _store.GetAsync("exec-outputs");

        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.AgentOutputs.Count);
        Assert.Equal("First agent response", retrieved.AgentOutputs["node-1"]);
        Assert.Equal("Full context here", retrieved.AccumulatedContext);
    }

    [Fact]
    public async Task WhenRecordWithEventsThenRoundTrips()
    {
        ExecutionRecord record = new()
        {
            Id = "exec-events",
            WorkflowId = "wf-1",
            Events =
            [
                new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.AgentStepStarted,
                    Data = "Starting agent"
                },
                new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.WorkflowOutput,
                    Data = "Agent output"
                }
            ]
        };

        await _store.SaveAsync(record);
        ExecutionRecord? retrieved = await _store.GetAsync("exec-events");

        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.Events.Count);
    }
}
