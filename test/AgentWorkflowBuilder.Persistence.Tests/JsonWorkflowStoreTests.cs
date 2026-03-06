using AgentWorkflowBuilder.Core.Models;
using AgentWorkflowBuilder.Persistence;

namespace AgentWorkflowBuilder.Persistence.Tests;

public class JsonWorkflowStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonWorkflowStore _store;

    public JsonWorkflowStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "awb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new JsonWorkflowStore(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public async Task WhenCreateWorkflowThenAssignsNewId()
    {
        WorkflowDefinition definition = new() { Name = "Test Workflow" };

        WorkflowDefinition created = await _store.CreateAsync(definition);

        Assert.NotEqual(definition.Id, created.Id);
        Assert.Equal("Test Workflow", created.Name);
    }

    [Fact]
    public async Task WhenCreateWorkflowThenSetsTimestamps()
    {
        DateTime before = DateTime.UtcNow;
        WorkflowDefinition created = await _store.CreateAsync(new() { Name = "Test" });
        DateTime after = DateTime.UtcNow;

        Assert.InRange(created.CreatedAt, before, after);
        Assert.InRange(created.UpdatedAt, before, after);
    }

    [Fact]
    public async Task WhenGetExistingWorkflowThenReturnsIt()
    {
        WorkflowDefinition created = await _store.CreateAsync(new() { Name = "Find Me" });

        WorkflowDefinition? retrieved = await _store.GetAsync(created.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal("Find Me", retrieved.Name);
    }

    [Fact]
    public async Task WhenGetNonexistentWorkflowThenReturnsNull()
    {
        WorkflowDefinition? result = await _store.GetAsync("nonexistent-id");

        Assert.Null(result);
    }

    [Fact]
    public async Task WhenListAllThenReturnsAllWorkflows()
    {
        await _store.CreateAsync(new() { Name = "WF 1" });
        await _store.CreateAsync(new() { Name = "WF 2" });
        await _store.CreateAsync(new() { Name = "WF 3" });

        IReadOnlyList<WorkflowDefinition> list = await _store.ListAsync();

        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task WhenListByUserIdThenFiltersResults()
    {
        await _store.CreateAsync(new() { Name = "User A WF", UserId = "user-a" });
        await _store.CreateAsync(new() { Name = "User B WF", UserId = "user-b" });
        await _store.CreateAsync(new() { Name = "User A WF 2", UserId = "user-a" });

        IReadOnlyList<WorkflowDefinition> userAList = await _store.ListAsync("user-a");

        Assert.Equal(2, userAList.Count);
        Assert.All(userAList, wf => Assert.Equal("user-a", wf.UserId));
    }

    [Fact]
    public async Task WhenListWithNoMatchingUserThenReturnsEmpty()
    {
        await _store.CreateAsync(new() { Name = "WF", UserId = "user-a" });

        IReadOnlyList<WorkflowDefinition> result = await _store.ListAsync("user-b");

        Assert.Empty(result);
    }

    [Fact]
    public async Task WhenUpdateWorkflowThenPersistsChanges()
    {
        WorkflowDefinition created = await _store.CreateAsync(new() { Name = "Original" });

        WorkflowDefinition updated = await _store.UpdateAsync(created with { Name = "Updated" });

        Assert.Equal("Updated", updated.Name);

        WorkflowDefinition? reloaded = await _store.GetAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Updated", reloaded.Name);
    }

    [Fact]
    public async Task WhenUpdateWorkflowThenUpdatesTimestamp()
    {
        WorkflowDefinition created = await _store.CreateAsync(new() { Name = "Original" });
        await Task.Delay(10); // Ensure timestamp differs

        WorkflowDefinition updated = await _store.UpdateAsync(created with { Name = "Updated" });

        Assert.True(updated.UpdatedAt >= created.UpdatedAt);
    }

    [Fact]
    public async Task WhenUpdateNonexistentWorkflowThenThrowsFileNotFound()
    {
        WorkflowDefinition workflow = new() { Id = "nonexistent" };

        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await _store.UpdateAsync(workflow));
    }

    [Fact]
    public async Task WhenDeleteWorkflowThenRemovesFromStore()
    {
        WorkflowDefinition created = await _store.CreateAsync(new() { Name = "Delete Me" });

        await _store.DeleteAsync(created.Id);

        WorkflowDefinition? result = await _store.GetAsync(created.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task WhenDeleteNonexistentWorkflowThenThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await _store.DeleteAsync("nonexistent"));
    }

    [Fact]
    public void WhenNullDataBasePathThenThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new JsonWorkflowStore(null!));
    }

    [Fact]
    public void WhenWhitespaceDataBasePathThenThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new JsonWorkflowStore("  "));
    }

    [Fact]
    public async Task WhenCreateNullDefinitionThenThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _store.CreateAsync(null!));
    }

    [Fact]
    public async Task WhenUpdateNullDefinitionThenThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _store.UpdateAsync(null!));
    }

    [Fact]
    public async Task WhenDeleteNullIdThenThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _store.DeleteAsync(null!));
    }

    [Fact]
    public async Task WhenWorkflowWithNodesThenRoundTrips()
    {
        WorkflowDefinition definition = new()
        {
            Name = "Complex WF",
            Nodes =
            [
                new WorkflowNode { NodeId = "n1", AgentId = "a1", Label = "Agent 1" },
                new WorkflowNode { NodeId = "n2", AgentId = "a2", Label = "Agent 2" }
            ],
            Edges =
            [
                new WorkflowEdge { SourceNodeId = "n1", TargetNodeId = "n2" }
            ]
        };

        WorkflowDefinition created = await _store.CreateAsync(definition);
        WorkflowDefinition? retrieved = await _store.GetAsync(created.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.Nodes.Count);
        Assert.Single(retrieved.Edges);
        Assert.Equal("n1", retrieved.Edges[0].SourceNodeId);
    }
}
