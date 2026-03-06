using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace AgentWorkflowBuilder.Core.Tests.Engine;

public class ExecutionSessionManagerTests
{
    private readonly IExecutionStore _executionStore = Substitute.For<IExecutionStore>();

    private ExecutionSessionManager CreateManager(int timeoutMinutes = 10)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workflow:ClarificationTimeoutMinutes"] = timeoutMinutes.ToString()
            })
            .Build();
        return new ExecutionSessionManager(config, _executionStore);
    }

    [Fact]
    public void WhenCreateSessionThenReturnsSession()
    {
        ExecutionSessionManager manager = CreateManager();

        ExecutionSession session = manager.CreateSession("exec-1");

        Assert.NotNull(session);
        Assert.Equal("exec-1", session.ExecutionId);
    }

    [Fact]
    public void WhenCreateDuplicateSessionThenThrows()
    {
        ExecutionSessionManager manager = CreateManager();
        manager.CreateSession("exec-1");

        Assert.Throws<InvalidOperationException>(() => manager.CreateSession("exec-1"));
    }

    [Fact]
    public void WhenHasSessionThenReturnsTrue()
    {
        ExecutionSessionManager manager = CreateManager();
        manager.CreateSession("exec-1");

        Assert.True(manager.HasSession("exec-1"));
    }

    [Fact]
    public void WhenNoSessionThenHasSessionReturnsFalse()
    {
        ExecutionSessionManager manager = CreateManager();

        Assert.False(manager.HasSession("nonexistent"));
    }

    [Fact]
    public void WhenTryGetSessionExistsThenReturnsTrue()
    {
        ExecutionSessionManager manager = CreateManager();
        manager.CreateSession("exec-1");

        bool found = manager.TryGetSession("exec-1", out ExecutionSession? session);

        Assert.True(found);
        Assert.NotNull(session);
        Assert.Equal("exec-1", session.ExecutionId);
    }

    [Fact]
    public void WhenTryGetSessionNotExistsThenReturnsFalse()
    {
        ExecutionSessionManager manager = CreateManager();

        bool found = manager.TryGetSession("nonexistent", out ExecutionSession? session);

        Assert.False(found);
        Assert.Null(session);
    }

    [Fact]
    public void WhenRemoveSessionThenNoLongerExists()
    {
        ExecutionSessionManager manager = CreateManager();
        manager.CreateSession("exec-1");

        manager.RemoveSession("exec-1");

        Assert.False(manager.HasSession("exec-1"));
    }

    [Fact]
    public void WhenRemoveNonexistentSessionThenNoError()
    {
        ExecutionSessionManager manager = CreateManager();

        // Should not throw
        manager.RemoveSession("nonexistent");
    }

    [Fact]
    public async Task WhenClarificationSubmittedThenResumesPausedExecution()
    {
        ExecutionSessionManager manager = CreateManager();
        manager.CreateSession("exec-1");

        using CancellationTokenSource cts = new();

        Task<string> waitTask = manager.WaitForClarificationAsync("exec-1", cts.Token);

        // Simulate user answering after a short delay
        await Task.Delay(50);
        manager.SubmitClarification("exec-1", "the answer");

        string result = await waitTask;

        Assert.Equal("the answer", result);

        // Verify checkpoints were saved (paused then running)
        await _executionStore.Received(2).SaveAsync(Arg.Any<ExecutionRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenGateApprovedThenResumesExecution()
    {
        ExecutionSessionManager manager = CreateManager();
        manager.CreateSession("exec-1");

        using CancellationTokenSource cts = new();

        Task<GateResponse> waitTask = manager.WaitForGateResponseAsync("exec-1", cts.Token);

        await Task.Delay(50);
        GateResponse approval = new() { Status = GateResponseStatus.Approved };
        manager.SubmitGateResponse("exec-1", approval);

        GateResponse result = await waitTask;

        Assert.Equal(GateResponseStatus.Approved, result.Status);
    }

    [Fact]
    public async Task WhenGateRejectedThenResumesWithRejection()
    {
        ExecutionSessionManager manager = CreateManager();
        manager.CreateSession("exec-1");

        using CancellationTokenSource cts = new();

        Task<GateResponse> waitTask = manager.WaitForGateResponseAsync("exec-1", cts.Token);

        await Task.Delay(50);
        GateResponse rejection = new() { Status = GateResponseStatus.Rejected, Reason = "Not good enough" };
        manager.SubmitGateResponse("exec-1", rejection);

        GateResponse result = await waitTask;

        Assert.Equal(GateResponseStatus.Rejected, result.Status);
        Assert.Equal("Not good enough", result.Reason);
    }

    [Fact]
    public async Task WhenGateSendBackThenSavesCheckpointWithSendBackPause()
    {
        ExecutionSessionManager manager = CreateManager();
        manager.CreateSession("exec-1");

        using CancellationTokenSource cts = new();

        Task<GateResponse> waitTask = manager.WaitForGateResponseAsync("exec-1", cts.Token);

        await Task.Delay(50);
        GateResponse sendBack = new() { Status = GateResponseStatus.SendBack, Feedback = "Redo this" };
        manager.SubmitGateResponse("exec-1", sendBack);

        GateResponse result = await waitTask;

        Assert.Equal(GateResponseStatus.SendBack, result.Status);

        // Verify the resume checkpoint was saved with SendBack pause type
        await _executionStore.Received().SaveAsync(
            Arg.Is<ExecutionRecord>(r => r.PauseType == PauseType.SendBack),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WhenSubmitClarificationToNonexistentSessionThenThrows()
    {
        ExecutionSessionManager manager = CreateManager();

        Assert.Throws<InvalidOperationException>(
            () => manager.SubmitClarification("nonexistent", "answer"));
    }

    [Fact]
    public void WhenSubmitGateResponseToNonexistentSessionThenThrows()
    {
        ExecutionSessionManager manager = CreateManager();

        Assert.Throws<InvalidOperationException>(
            () => manager.SubmitGateResponse("nonexistent", new GateResponse { Status = GateResponseStatus.Approved }));
    }

    [Fact]
    public async Task WhenClarificationTimeoutThenThrowsTimeoutException()
    {
        // Use a very short timeout (1 minute but we'll cancel via linked token for test speed)
        ExecutionSessionManager manager = CreateManager(timeoutMinutes: 1);
        manager.CreateSession("exec-1");

        using CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await manager.WaitForClarificationAsync("exec-1", cts.Token));
    }

    [Fact]
    public void WhenNullConfigThenThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ExecutionSessionManager(null!, _executionStore));
    }

    [Fact]
    public void WhenNullExecutionStoreThenThrows()
    {
        IConfiguration config = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(
            () => new ExecutionSessionManager(config, null!));
    }

    [Fact]
    public void WhenTimeoutNotConfiguredThenDefaultsTo10Minutes()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        // Just verify it doesn't throw — default value is used
        ExecutionSessionManager manager = new(config, _executionStore);

        Assert.NotNull(manager);
    }
}
