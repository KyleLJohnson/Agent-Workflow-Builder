using AgentWorkflowBuilder.Agents;
using AgentWorkflowBuilder.Api;
using AgentWorkflowBuilder.Api.Hubs;
using AgentWorkflowBuilder.Api.Services;
using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using AgentWorkflowBuilder.Persistence;
using Azure.Storage.Blobs;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.Cosmos;
using Microsoft.Identity.Web;

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

// --- Authentication (Microsoft Entra ID) ---
// Only enable if AzureAd config is provided with a real tenant/client ID.
bool entraAuthEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:TenantId"])
    && builder.Configuration["AzureAd:TenantId"] != "<tenant-id>";

if (entraAuthEnabled)
{
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddInMemoryTokenCaches();

    builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.Events ??= new JwtBearerEvents();
        JwtBearerEvents existingEvents = options.Events;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // SignalR passes the token as a query parameter for WebSocket upgrade
                PathString path = context.HttpContext.Request.Path;
                if (path.StartsWithSegments("/hubs"))
                {
                    string? token = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token))
                    {
                        context.Token = token;
                    }
                }
                return existingEvents.OnMessageReceived?.Invoke(context) ?? Task.CompletedTask;
            }
        };
    });

    builder.Services.AddAuthorization();
}

// --- Register services ---
builder.Services.AddSingleton<CopilotClient>();
builder.Services.AddSingleton(sp => new CopilotProviderFactory(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<IAgentRegistry>(new JsonAgentRegistry(dataBasePath));

// Workflow store: use Cosmos DB when configured, otherwise JSON files
string cosmosConnectionString = builder.Configuration["CosmosDb:ConnectionString"] ?? string.Empty;
bool cosmosEnabled = !string.IsNullOrWhiteSpace(cosmosConnectionString);
CosmosClient? cosmosClient = null;

if (cosmosEnabled)
{
    cosmosClient = new CosmosClient(cosmosConnectionString);
    builder.Services.AddSingleton(cosmosClient);
    string cosmosDbName = builder.Configuration["CosmosDb:DatabaseName"] ?? "AgentWorkflowBuilder";
    string wfContainer = builder.Configuration["CosmosDb:WorkflowContainerName"] ?? "workflows";
    string execContainer = builder.Configuration["CosmosDb:ExecutionContainerName"] ?? "executions";

    builder.Services.AddSingleton<IWorkflowStore>(sp =>
        new CosmosWorkflowStore(cosmosClient, cosmosDbName, wfContainer,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<CosmosWorkflowStore>()));
    builder.Services.AddSingleton<IExecutionStore>(sp =>
        new CosmosExecutionStore(cosmosClient, cosmosDbName, execContainer,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<CosmosExecutionStore>()));
}
else
{
    builder.Services.AddSingleton<IWorkflowStore>(new JsonWorkflowStore(dataBasePath));
    builder.Services.AddSingleton<IExecutionStore>(new JsonExecutionStore(dataBasePath));
}

var mcpManager = new McpClientManager(dataBasePath);
builder.Services.AddSingleton<IMcpConfigStore>(mcpManager);
builder.Services.AddSingleton<IMcpClientManager>(mcpManager);
builder.Services.AddSingleton<ICopilotSessionFactory, CopilotSessionFactory>();
builder.Services.AddSingleton<ExecutionSessionManager>();
builder.Services.AddSingleton<IWorkflowEngine, WorkflowEngine>();
builder.Services.AddHostedService<ExecutionRecoveryService>();

// --- Blob polling service (optional) ---
if (string.Equals(builder.Configuration["AzureBlobPlans:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
{
    string blobConnectionString = builder.Configuration["AzureBlobPlans:ConnectionString"]
        ?? throw new InvalidOperationException("AzureBlobPlans:ConnectionString is required when blob polling is enabled.");
    builder.Services.AddSingleton(new BlobServiceClient(blobConnectionString));
    builder.Services.AddHostedService<BlobPlanPollingService>();
}

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

if (entraAuthEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

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
// Workflow Endpoints (user-scoped when auth is enabled)
// =============================================================================

// Helper to extract userId — returns null when auth is not enabled
string? GetUserIdFromContext(HttpContext ctx)
{
    try { return UserContext.GetUserId(ctx.User); }
    catch (UnauthorizedAccessException) { return null; }
}

app.MapGet("/api/workflows", async (HttpContext httpContext, IWorkflowStore store, CancellationToken ct) =>
{
    string? userId = GetUserIdFromContext(httpContext);
    IReadOnlyList<WorkflowDefinition> workflows = await store.ListAsync(userId, ct);
    return Results.Ok(workflows);
});

app.MapGet("/api/workflows/{id}", async (string id, HttpContext httpContext, IWorkflowStore store, CancellationToken ct) =>
{
    WorkflowDefinition? workflow = await store.GetAsync(id, ct);
    if (workflow is null) return Results.NotFound();
    string? userId = GetUserIdFromContext(httpContext);
    if (userId is not null && workflow.UserId != userId) return Results.NotFound();
    return Results.Ok(workflow);
});

app.MapPost("/api/workflows", async (WorkflowDefinition definition, HttpContext httpContext, IWorkflowStore store, CancellationToken ct) =>
{
    string? userId = GetUserIdFromContext(httpContext);
    WorkflowDefinition toCreate = userId is not null ? definition with { UserId = userId } : definition;
    WorkflowDefinition created = await store.CreateAsync(toCreate, ct);
    return Results.Created($"/api/workflows/{created.Id}", created);
});

app.MapPut("/api/workflows/{id}", async (string id, WorkflowDefinition definition, HttpContext httpContext, IWorkflowStore store, CancellationToken ct) =>
{
    try
    {
        WorkflowDefinition? existing = await store.GetAsync(id, ct);
        if (existing is null) return Results.NotFound();
        string? userId = GetUserIdFromContext(httpContext);
        if (userId is not null && existing.UserId != userId) return Results.NotFound();
        WorkflowDefinition updated = await store.UpdateAsync(definition with { Id = id }, ct);
        return Results.Ok(updated);
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
});

app.MapDelete("/api/workflows/{id}", async (string id, HttpContext httpContext, IWorkflowStore store, CancellationToken ct) =>
{
    try
    {
        WorkflowDefinition? existing = await store.GetAsync(id, ct);
        if (existing is null) return Results.NotFound();
        string? userId = GetUserIdFromContext(httpContext);
        if (userId is not null && existing.UserId != userId) return Results.NotFound();
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
    HttpContext httpContext,
    IWorkflowStore store,
    IWorkflowEngine engine,
    CancellationToken ct) =>
{
    WorkflowDefinition? workflow = await store.GetAsync(id, ct);
    if (workflow is null) return Results.NotFound();
    string? userId = GetUserIdFromContext(httpContext);
    if (userId is not null && workflow.UserId != userId) return Results.NotFound();

    try
    {
        WorkflowExecutionEvent result = await engine.ExecuteAsync(workflow, request.Content, ct: ct);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// =============================================================================
// Execution History Endpoints
// =============================================================================

app.MapGet("/api/executions", async (HttpContext httpContext, IExecutionStore executionStore, IWorkflowStore workflowStore, CancellationToken ct) =>
{
    string? userId = GetUserIdFromContext(httpContext);
    IReadOnlyList<WorkflowDefinition> workflows = await workflowStore.ListAsync(userId, ct);
    List<ExecutionRecord> allRecords = [];
    foreach (WorkflowDefinition wf in workflows)
    {
        IReadOnlyList<ExecutionRecord> records = await executionStore.ListByWorkflowAsync(wf.Id, ct);
        allRecords.AddRange(records);
    }
    allRecords.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
    return Results.Ok(allRecords);
});

app.MapGet("/api/executions/paused", async (IExecutionStore executionStore, CancellationToken ct) =>
{
    IReadOnlyList<ExecutionRecord> paused = await executionStore.GetPausedAsync(ct);
    return Results.Ok(paused);
});

app.MapGet("/api/executions/{id}", async (string id, IExecutionStore executionStore, CancellationToken ct) =>
{
    ExecutionRecord? record = await executionStore.GetAsync(id, ct);
    return record is not null ? Results.Ok(record) : Results.NotFound();
});

app.MapGet("/api/workflows/{workflowId}/executions", async (string workflowId, IExecutionStore executionStore, CancellationToken ct) =>
{
    IReadOnlyList<ExecutionRecord> records = await executionStore.ListByWorkflowAsync(workflowId, ct);
    return Results.Ok(records);
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
// User Info Endpoint (only when Entra auth is enabled)
// =============================================================================

if (entraAuthEnabled)
{
    app.MapGet("/api/me", (HttpContext httpContext) =>
    {
        string userId = UserContext.GetUserId(httpContext.User);
        string? displayName = httpContext.User.FindFirst("name")?.Value;
        string? email = httpContext.User.FindFirst("preferred_username")?.Value;
        return Results.Ok(new { userId, displayName, email });
    }).RequireAuthorization();
}

// =============================================================================
// SignalR Hub (streaming execution)
// =============================================================================

var hubEndpoint = app.MapHub<WorkflowHub>("/hubs/workflow");
if (entraAuthEnabled)
{
    hubEndpoint.RequireAuthorization();
}

app.Run();
