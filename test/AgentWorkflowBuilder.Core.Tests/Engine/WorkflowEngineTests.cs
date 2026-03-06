using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AgentWorkflowBuilder.Core.Tests.Engine;

public class WorkflowEngineTests
{
    private readonly IAgentRegistry _agentRegistry = Substitute.For<IAgentRegistry>();
    private readonly ICopilotSessionFactory _sessionFactory = Substitute.For<ICopilotSessionFactory>();
    private readonly IExecutionStore _executionStore = Substitute.For<IExecutionStore>();
    private readonly ILogger<WorkflowEngine> _logger = Substitute.For<ILogger<WorkflowEngine>>();

    private WorkflowEngine CreateEngine(int maxLoopIterations = 3)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workflow:MaxLoopIterations"] = maxLoopIterations.ToString(),
                ["Workflow:ClarificationTimeoutMinutes"] = "10"
            })
            .Build();

        ExecutionSessionManager sessionManager = new(config, _executionStore);

        return new WorkflowEngine(
            _agentRegistry,
            _sessionFactory,
            sessionManager,
            _executionStore,
            config,
            _logger);
    }

    private static AgentDefinition CreateAgent(string id = "agent-1", string name = "Test Agent") =>
        new()
        {
            Id = id,
            Name = name,
            Description = "A test agent",
            SystemInstructions = "You are a test agent."
        };

    [Fact]
    public async Task WhenWorkflowIsNullThenThrowsArgumentNull()
    {
        WorkflowEngine engine = CreateEngine();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await engine.ExecuteAsync(null!, "input"));
    }

    [Fact]
    public async Task WhenInputMessageIsNullThenThrowsArgumentNullException()
    {
        WorkflowEngine engine = CreateEngine();
        WorkflowDefinition workflow = new() { Nodes = [new WorkflowNode { NodeId = "n1" }] };

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await engine.ExecuteAsync(workflow, null!));
    }

    [Fact]
    public async Task WhenInputMessageIsWhitespaceThenThrowsArgumentException()
    {
        WorkflowEngine engine = CreateEngine();
        WorkflowDefinition workflow = new() { Nodes = [new WorkflowNode { NodeId = "n1" }] };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await engine.ExecuteAsync(workflow, "  "));
    }

    [Fact]
    public async Task WhenEmptyWorkflowThenThrowsInvalidOperation()
    {
        WorkflowEngine engine = CreateEngine();
        WorkflowDefinition workflow = new() { Nodes = [] };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await engine.ExecuteAsync(workflow, "hello"));
    }

    [Fact]
    public async Task WhenAgentNotFoundThenThrowsInvalidOperation()
    {
        WorkflowEngine engine = CreateEngine();
        WorkflowDefinition workflow = new()
        {
            Nodes = [new WorkflowNode { NodeId = "n1", AgentId = "missing-agent" }],
            Edges = []
        };

        _agentRegistry.GetAsync("missing-agent", Arg.Any<CancellationToken>())
            .Returns((AgentDefinition?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await engine.ExecuteAsync(workflow, "hello"));
    }

    [Fact]
    public async Task WhenSessionFactoryThrowsThenReturnsErrorEvent()
    {
        AgentDefinition agent = CreateAgent();
        WorkflowEngine engine = CreateEngine();

        WorkflowDefinition workflow = new()
        {
            Nodes = [new WorkflowNode { NodeId = "n1", AgentId = agent.Id }],
            Edges = []
        };

        _agentRegistry.GetAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        _sessionFactory.CreateSessionAsync(
            Arg.Any<AgentDefinition>(),
            Arg.Any<Action<WorkflowExecutionEvent>?>(),
            Arg.Any<Func<UserInputRequest, UserInputInvocation, Task<UserInputResponse>>?>(),
            Arg.Any<CancellationToken>())
        .ThrowsAsync(new InvalidOperationException("Session creation failed"));

        // Engine catches the exception and returns an error event
        WorkflowExecutionEvent result = await engine.ExecuteAsync(workflow, "hello");

        Assert.Equal(ExecutionEventType.Error, result.EventType);
        Assert.Contains("Session creation failed", result.Data);
    }

    [Fact]
    public async Task WhenSingleNodeWorkflowThenCallsFactoryWithCorrectAgent()
    {
        AgentDefinition agent = CreateAgent();
        WorkflowEngine engine = CreateEngine();

        WorkflowDefinition workflow = new()
        {
            Nodes = [new WorkflowNode { NodeId = "n1", AgentId = agent.Id }],
            Edges = []
        };

        _agentRegistry.GetAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);

        // Factory will throw because we can't mock the sealed CopilotSession,
        // but we can verify the factory received the correct agent definition
        _sessionFactory.CreateSessionAsync(
            Arg.Any<AgentDefinition>(),
            Arg.Any<Action<WorkflowExecutionEvent>?>(),
            Arg.Any<Func<UserInputRequest, UserInputInvocation, Task<UserInputResponse>>?>(),
            Arg.Any<CancellationToken>())
        .ThrowsAsync(new InvalidOperationException("test"));

        try { await engine.ExecuteAsync(workflow, "hello"); } catch { /* expected */ }

        await _sessionFactory.Received(1).CreateSessionAsync(
            Arg.Is<AgentDefinition>(a => a.Id == agent.Id && a.Name == agent.Name),
            Arg.Any<Action<WorkflowExecutionEvent>?>(),
            Arg.Any<Func<UserInputRequest, UserInputInvocation, Task<UserInputResponse>>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenCancelledThenThrowsOperationCancelled()
    {
        AgentDefinition agent = CreateAgent();
        WorkflowEngine engine = CreateEngine();

        WorkflowDefinition workflow = new()
        {
            Nodes = [new WorkflowNode { NodeId = "n1", AgentId = agent.Id }],
            Edges = []
        };

        _agentRegistry.GetAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await engine.ExecuteAsync(workflow, "hello", ct: cts.Token));
    }

    [Fact]
    public async Task WhenExecutionStartsThenSavesInitialRecord()
    {
        AgentDefinition agent = CreateAgent();
        WorkflowEngine engine = CreateEngine();

        WorkflowDefinition workflow = new()
        {
            Id = "wf-1",
            Nodes = [new WorkflowNode { NodeId = "n1", AgentId = agent.Id }],
            Edges = []
        };

        _agentRegistry.GetAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);

        // Factory throws, but the engine should have saved at least the initial record
        _sessionFactory.CreateSessionAsync(
            Arg.Any<AgentDefinition>(),
            Arg.Any<Action<WorkflowExecutionEvent>?>(),
            Arg.Any<Func<UserInputRequest, UserInputInvocation, Task<UserInputResponse>>?>(),
            Arg.Any<CancellationToken>())
        .ThrowsAsync(new InvalidOperationException("test"));

        try { await engine.ExecuteAsync(workflow, "hello"); } catch { /* expected */ }

        // Verify at least 1 save call (the initial Running record)
        await _executionStore.Received().SaveAsync(
            Arg.Is<ExecutionRecord>(r => r.WorkflowId == "wf-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenGateNodeThenYieldsGateAwaitingEvent()
    {
        WorkflowEngine engine = CreateEngine();

        WorkflowDefinition workflow = new()
        {
            Nodes =
            [
                new WorkflowNode
                {
                    NodeId = "gate-1",
                    NodeType = "gate",
                    GateConfig = new GateConfiguration
                    {
                        GateType = GateType.Approval,
                        Instructions = "Please approve"
                    }
                }
            ],
            Edges = []
        };

        // Gate will wait for response — we need to submit it async
        // Since we can't easily inject into the session manager from here,
        // let's just verify the streaming starts correctly
        List<WorkflowExecutionEvent> events = [];
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));

        try
        {
            await foreach (WorkflowExecutionEvent evt in engine.ExecuteStreamingAsync(workflow, "input", ct: cts.Token))
            {
                events.Add(evt);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected — gate times out
        }
        catch (TimeoutException)
        {
            // Expected — gate timeout
        }

        Assert.Contains(events, e => e.EventType == ExecutionEventType.GateAwaitingApproval);
    }

    [Fact]
    public async Task WhenAutoApproveGatesThenSkipsWaitingAndEmitsAutoApprovedEvent()
    {
        WorkflowEngine engine = CreateEngine();

        WorkflowDefinition workflow = new()
        {
            Nodes =
            [
                new WorkflowNode
                {
                    NodeId = "gate-1",
                    NodeType = "gate",
                    GateConfig = new GateConfiguration
                    {
                        GateType = GateType.Approval,
                        Instructions = "Please approve"
                    }
                }
            ],
            Edges = []
        };

        List<WorkflowExecutionEvent> events = [];
        await foreach (WorkflowExecutionEvent evt in engine.ExecuteStreamingAsync(workflow, "test input", autoApproveGates: true))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e.EventType == ExecutionEventType.GateAutoApproved);
        Assert.DoesNotContain(events, e => e.EventType == ExecutionEventType.GateAwaitingApproval);

        WorkflowExecutionEvent autoApprovedEvent = events.First(e => e.EventType == ExecutionEventType.GateAutoApproved);
        Assert.Equal("gate-1", autoApprovedEvent.NodeId);
        Assert.Equal("test input", autoApprovedEvent.Data);
        Assert.Equal("Approval", autoApprovedEvent.GateType);
    }
}
