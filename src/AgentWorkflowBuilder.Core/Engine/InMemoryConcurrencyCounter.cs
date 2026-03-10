using System.Collections.Concurrent;
using AgentWorkflowBuilder.Core.Interfaces;

namespace AgentWorkflowBuilder.Core.Engine;

/// <summary>
/// In-memory concurrency counter for local development (single-instance).
/// </summary>
public sealed class InMemoryConcurrencyCounter : IConcurrencyCounter
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public Task<bool> TryIncrementAsync(string userId, int maxConcurrent, CancellationToken ct = default)
    {
        int current = _counts.AddOrUpdate(userId, 1, (_, c) =>
        {
            if (c >= maxConcurrent) return c;
            return c + 1;
        });

        return Task.FromResult(current <= maxConcurrent);
    }

    public Task DecrementAsync(string userId, CancellationToken ct = default)
    {
        _counts.AddOrUpdate(userId, 0, (_, c) => Math.Max(0, c - 1));
        return Task.CompletedTask;
    }
}
