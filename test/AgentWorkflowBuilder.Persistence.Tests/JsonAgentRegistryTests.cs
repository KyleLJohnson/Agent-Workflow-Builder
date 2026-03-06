using System.Text.Json;
using AgentWorkflowBuilder.Core.Models;
using AgentWorkflowBuilder.Persistence;

namespace AgentWorkflowBuilder.Persistence.Tests;

public class JsonAgentRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonAgentRegistry _registry;

    public JsonAgentRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "awb-agent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _registry = new JsonAgentRegistry(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    private async Task SeedBuiltInAgent(string id, string name)
    {
        string builtInDir = Path.Combine(_tempDir, "agents", "builtin");
        Directory.CreateDirectory(builtInDir);

        AgentDefinition agent = new()
        {
            Id = id,
            Name = name,
            IsBuiltIn = true,
            Description = $"Built-in {name}"
        };

        string json = JsonSerializer.Serialize(agent, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(builtInDir, $"{id}.json"), json);
    }

    [Fact]
    public async Task WhenCreateCustomAgentThenAssignsNewId()
    {
        AgentDefinition definition = new() { Name = "My Agent" };

        AgentDefinition created = await _registry.CreateAsync(definition);

        Assert.NotEqual(definition.Id, created.Id);
        Assert.Equal("My Agent", created.Name);
        Assert.False(created.IsBuiltIn);
    }

    [Fact]
    public async Task WhenCreateAgentThenSetsTimestamps()
    {
        DateTime before = DateTime.UtcNow;
        AgentDefinition created = await _registry.CreateAsync(new() { Name = "Test" });
        DateTime after = DateTime.UtcNow;

        Assert.InRange(created.CreatedAt, before, after);
        Assert.InRange(created.UpdatedAt, before, after);
    }

    [Fact]
    public async Task WhenCreateAgentThenForcesIsBuiltInFalse()
    {
        AgentDefinition definition = new() { Name = "Fake Built-in", IsBuiltIn = true };

        AgentDefinition created = await _registry.CreateAsync(definition);

        Assert.False(created.IsBuiltIn);
    }

    [Fact]
    public async Task WhenGetCustomAgentThenReturnsIt()
    {
        AgentDefinition created = await _registry.CreateAsync(new() { Name = "Find Me" });

        AgentDefinition? retrieved = await _registry.GetAsync(created.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal("Find Me", retrieved.Name);
    }

    [Fact]
    public async Task WhenGetBuiltInAgentThenReturnsIt()
    {
        await SeedBuiltInAgent("builtin-1", "Summarizer");

        AgentDefinition? retrieved = await _registry.GetAsync("builtin-1");

        Assert.NotNull(retrieved);
        Assert.Equal("Summarizer", retrieved.Name);
        Assert.True(retrieved.IsBuiltIn);
    }

    [Fact]
    public async Task WhenGetNonexistentAgentThenReturnsNull()
    {
        AgentDefinition? result = await _registry.GetAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task WhenListThenReturnsBothBuiltInAndCustom()
    {
        await SeedBuiltInAgent("builtin-1", "Summarizer");
        await _registry.CreateAsync(new() { Name = "Custom Agent" });

        IReadOnlyList<AgentDefinition> agents = await _registry.ListAsync();

        Assert.Equal(2, agents.Count);
    }

    [Fact]
    public async Task WhenUpdateCustomAgentThenPersistsChanges()
    {
        AgentDefinition created = await _registry.CreateAsync(new() { Name = "Original" });

        AgentDefinition updated = await _registry.UpdateAsync(
            created with { Name = "Updated", Description = "New description" });

        Assert.Equal("Updated", updated.Name);

        AgentDefinition? reloaded = await _registry.GetAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Updated", reloaded.Name);
        Assert.Equal("New description", reloaded.Description);
    }

    [Fact]
    public async Task WhenUpdateCustomAgentThenUpdatesTimestamp()
    {
        AgentDefinition created = await _registry.CreateAsync(new() { Name = "Original" });
        await Task.Delay(10);

        AgentDefinition updated = await _registry.UpdateAsync(created with { Name = "Updated" });

        Assert.True(updated.UpdatedAt >= created.UpdatedAt);
    }

    [Fact]
    public async Task WhenUpdateBuiltInAgentThenThrowsInvalidOperation()
    {
        await SeedBuiltInAgent("builtin-1", "Summarizer");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _registry.UpdateAsync(new() { Id = "builtin-1", Name = "Hacked" }));
    }

    [Fact]
    public async Task WhenUpdateNonexistentCustomAgentThenThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await _registry.UpdateAsync(new() { Id = "nonexistent" }));
    }

    [Fact]
    public async Task WhenDeleteCustomAgentThenRemovesFromStore()
    {
        AgentDefinition created = await _registry.CreateAsync(new() { Name = "Delete Me" });

        await _registry.DeleteAsync(created.Id);

        AgentDefinition? result = await _registry.GetAsync(created.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task WhenDeleteBuiltInAgentThenThrowsInvalidOperation()
    {
        await SeedBuiltInAgent("builtin-1", "Summarizer");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _registry.DeleteAsync("builtin-1"));
    }

    [Fact]
    public async Task WhenDeleteNonexistentAgentThenThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await _registry.DeleteAsync("nonexistent"));
    }

    [Fact]
    public async Task WhenIsBuiltInThenReturnsTrueForBuiltIn()
    {
        await SeedBuiltInAgent("builtin-1", "Summarizer");

        bool result = await _registry.IsBuiltInAsync("builtin-1");

        Assert.True(result);
    }

    [Fact]
    public async Task WhenIsBuiltInForCustomAgentThenReturnsFalse()
    {
        AgentDefinition created = await _registry.CreateAsync(new() { Name = "Custom" });

        bool result = await _registry.IsBuiltInAsync(created.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task WhenIsBuiltInForNonexistentThenReturnsFalse()
    {
        bool result = await _registry.IsBuiltInAsync("nonexistent");

        Assert.False(result);
    }

    [Fact]
    public void WhenNullDataBasePathThenThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new JsonAgentRegistry(null!));
    }

    [Fact]
    public void WhenWhitespaceDataBasePathThenThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new JsonAgentRegistry("  "));
    }

    [Fact]
    public async Task WhenCreateNullDefinitionThenThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _registry.CreateAsync(null!));
    }

    [Fact]
    public async Task WhenUpdateNullDefinitionThenThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _registry.UpdateAsync(null!));
    }

    [Fact]
    public async Task WhenDeleteNullIdThenThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _registry.DeleteAsync(null!));
    }

    [Fact]
    public async Task WhenAgentWithMcpServerIdsThenRoundTrips()
    {
        AgentDefinition definition = new()
        {
            Name = "MCP Agent",
            McpServerIds = ["server-1", "server-2"],
            AllowClarification = false,
            AgentType = "planner"
        };

        AgentDefinition created = await _registry.CreateAsync(definition);
        AgentDefinition? retrieved = await _registry.GetAsync(created.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.McpServerIds.Count);
        Assert.Contains("server-1", retrieved.McpServerIds);
        Assert.False(retrieved.AllowClarification);
        Assert.Equal("planner", retrieved.AgentType);
    }

    [Fact]
    public async Task WhenBuiltInCheckedFirstThenPrioritized()
    {
        // If same ID exists in both built-in and custom, built-in wins
        string sharedId = "shared-id";
        await SeedBuiltInAgent(sharedId, "Built-In Version");

        // Manually create a custom agent file with the same ID
        string customDir = Path.Combine(_tempDir, "agents", "custom");
        Directory.CreateDirectory(customDir);
        AgentDefinition customAgent = new()
        {
            Id = sharedId,
            Name = "Custom Version"
        };
        string json = JsonSerializer.Serialize(customAgent, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(customDir, $"{sharedId}.json"), json);

        AgentDefinition? retrieved = await _registry.GetAsync(sharedId);

        Assert.NotNull(retrieved);
        Assert.Equal("Built-In Version", retrieved.Name);
    }
}
