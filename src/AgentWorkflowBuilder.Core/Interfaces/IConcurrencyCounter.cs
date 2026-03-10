namespace AgentWorkflowBuilder.Core.Interfaces;

/// <summary>
/// Distributed concurrency counter for rate-limiting per-user execution counts.
/// Implemented by Cosmos DB with optimistic concurrency in production, or in-memory for local development.
/// </summary>
public interface IConcurrencyCounter
{
    /// <summary>
    /// Attempts to increment the active execution count for a user.
    /// Returns true if successfully incremented (within limit), false if the limit was reached.
    /// </summary>
    Task<bool> TryIncrementAsync(string userId, int maxConcurrent, CancellationToken ct = default);

    /// <summary>
    /// Decrements the active execution count for a user.
    /// </summary>
    Task DecrementAsync(string userId, CancellationToken ct = default);
}
