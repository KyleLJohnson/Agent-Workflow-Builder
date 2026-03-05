using AgentWorkflowBuilder.Agents;
using AgentWorkflowBuilder.Api.Hubs;
using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using AgentWorkflowBuilder.Persistence;
using GitHub.Copilot.SDK;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---
var configuredDataPath = builder.Configuration["Data:BasePath"];
var dataBasePath = string.IsNullOrWhiteSpace(configuredDataPath)
    ? Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "data")
    : configuredDataPath;

// Normalize data path
dataBasePath = Path.GetFullPath(dataBasePath);

// --- Seed built-in agents ---
AgentSeeder.SeedBuiltInAgents(dataBasePath);

// --- Register services ---
builder.Services.AddSingleton<CopilotClient>();
builder.Services.AddSingleton(sp => new CopilotProviderFactory(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<IAgentRegistry>(new JsonAgentRegistry(dataBasePath));
builder.Services.AddSingleton<IWorkflowStore>(new JsonWorkflowStore(dataBasePath));
var mcpManager = new McpClientManager(dataBasePath);
builder.Services.AddSingleton<IMcpConfigStore>(mcpManager);
builder.Services.AddSingleton<IMcpClientManager>(mcpManager);
builder.Services.AddSingleton<ICopilotSessionFactory, CopilotSessionFactory>();
builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
builder.Services.AddSignalR(options =>
{
    // Increase timeouts — AI workflow execution can take 30-60+ seconds
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                  "http://localhost:5173",
                  "http://localhost:5174",
                  "http://localhost:5175") // Vite dev server (auto-increments port)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors();

// --- Initialize MCP config (loads server definitions from mcp.json) ---
var mcpConfigStore = app.Services.GetRequiredService<IMcpConfigStore>();
await ((McpClientManager)mcpConfigStore).InitializeAsync();

// =============================================================================
// Agent Endpoints
// =============================================================================

app.MapGet("/api/agents", async (IAgentRegistry registry, CancellationToken ct) =>
{
    var agents = await registry.ListAsync(ct);
    return Results.Ok(agents);
});

app.MapGet("/api/agents/{id}", async (string id, IAgentRegistry registry, CancellationToken ct) =>
{
    var agent = await registry.GetAsync(id, ct);
    return agent is not null ? Results.Ok(agent) : Results.NotFound();
});

app.MapPost("/api/agents", async (AgentDefinition definition, IAgentRegistry registry, CancellationToken ct) =>
{
    var created = await registry.CreateAsync(definition, ct);
    return Results.Created($"/api/agents/{created.Id}", created);
});

app.MapPut("/api/agents/{id}", async (string id, AgentDefinition definition, IAgentRegistry registry, CancellationToken ct) =>
{
    try
    {
        var updated = await registry.UpdateAsync(definition with { Id = id }, ct);
        return Results.Ok(updated);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
});

app.MapDelete("/api/agents/{id}", async (string id, IAgentRegistry registry, CancellationToken ct) =>
{
    try
    {
        await registry.DeleteAsync(id, ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
});

// =============================================================================
// Workflow Endpoints
// =============================================================================

app.MapGet("/api/workflows", async (IWorkflowStore store, CancellationToken ct) =>
{
    var workflows = await store.ListAsync(ct);
    return Results.Ok(workflows);
});

app.MapGet("/api/workflows/{id}", async (string id, IWorkflowStore store, CancellationToken ct) =>
{
    var workflow = await store.GetAsync(id, ct);
    return workflow is not null ? Results.Ok(workflow) : Results.NotFound();
});

app.MapPost("/api/workflows", async (WorkflowDefinition definition, IWorkflowStore store, CancellationToken ct) =>
{
    var created = await store.CreateAsync(definition, ct);
    return Results.Created($"/api/workflows/{created.Id}", created);
});

app.MapPut("/api/workflows/{id}", async (string id, WorkflowDefinition definition, IWorkflowStore store, CancellationToken ct) =>
{
    try
    {
        var updated = await store.UpdateAsync(definition with { Id = id }, ct);
        return Results.Ok(updated);
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
});

app.MapDelete("/api/workflows/{id}", async (string id, IWorkflowStore store, CancellationToken ct) =>
{
    try
    {
        await store.DeleteAsync(id, ct);
        return Results.NoContent();
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
});

// =============================================================================
// Workflow Execution (non-streaming, for batch/file input)
// =============================================================================

app.MapPost("/api/workflows/{id}/execute", async (
    string id,
    WorkflowExecutionRequest request,
    IWorkflowStore store,
    IWorkflowEngine engine,
    CancellationToken ct) =>
{
    var workflow = await store.GetAsync(id, ct);
    if (workflow is null) return Results.NotFound();

    try
    {
        var result = await engine.ExecuteAsync(workflow, request.Content, ct);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// =============================================================================
// MCP Server Endpoints
// =============================================================================

app.MapGet("/api/mcp/servers", async (IMcpClientManager mcp, CancellationToken ct) =>
{
    var servers = await mcp.ListServersAsync(ct);
    return Results.Ok(servers);
});

app.MapPost("/api/mcp/servers", async (McpServerConfig config, IMcpClientManager mcp, CancellationToken ct) =>
{
    var created = await mcp.AddServerAsync(config, ct);
    return Results.Created($"/api/mcp/servers/{created.Id}", created);
});

app.MapPut("/api/mcp/servers/{id}", async (string id, McpServerConfig config, IMcpClientManager mcp, CancellationToken ct) =>
{
    try
    {
        var updated = await mcp.UpdateServerAsync(config with { Id = id }, ct);
        return Results.Ok(updated);
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
});

app.MapDelete("/api/mcp/servers/{id}", async (string id, IMcpClientManager mcp, CancellationToken ct) =>
{
    try
    {
        await mcp.RemoveServerAsync(id, ct);
        return Results.NoContent();
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
});

app.MapGet("/api/mcp/tools", async (IMcpClientManager mcp, CancellationToken ct) =>
{
    var tools = await mcp.ListToolsAsync(ct);
    return Results.Ok(tools);
});

app.MapPost("/api/mcp/tools/call", async (McpToolCallRequest request, IMcpClientManager mcp, CancellationToken ct) =>
{
    var result = await mcp.CallToolAsync(request, ct);
    return result.IsSuccess ? Results.Ok(result) : Results.UnprocessableEntity(result);
});

// =============================================================================
// SignalR Hub (streaming execution)
// =============================================================================

app.MapHub<WorkflowHub>("/hubs/workflow");

app.Run();
