namespace AgentWorkflowBuilder.Persistence;

/// <summary>
/// Centralised Cosmos DB SQL query constants used by the persistence layer.
/// Keep all raw SQL here so queries are easy to find, review, and test.
/// </summary>
internal static class CosmosQueries
{
    // ── Execution queries ───────────────────────────────────────────────
    internal const string ExecutionById =
        "SELECT * FROM c WHERE c.id = @id";

    internal const string ExecutionsByWorkflow =
        "SELECT * FROM c WHERE c.workflowId = @wfId ORDER BY c.startedAt DESC";

    internal const string PausedExecutions =
        "SELECT * FROM c WHERE c.status = 'Paused'";

    // ── Workflow queries ────────────────────────────────────────────────
    internal const string WorkflowsByUser =
        "SELECT * FROM c WHERE c.userId = @userId ORDER BY c.updatedAt DESC";

    internal const string AllWorkflows =
        "SELECT * FROM c ORDER BY c.updatedAt DESC";

    internal const string WorkflowById =
        "SELECT * FROM c WHERE c.id = @id";

    // ── Session queries ─────────────────────────────────────────────────
    internal const string LatestClarification =
        "SELECT TOP 1 * FROM c WHERE c.executionId = @execId AND c.documentType = 'ClarificationResponse' ORDER BY c.createdAt DESC";

    internal const string LatestGateResponse =
        "SELECT TOP 1 * FROM c WHERE c.executionId = @execId AND c.documentType = 'GateResponse' ORDER BY c.createdAt DESC";
}
