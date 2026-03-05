using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using GitHub.Copilot.SDK;

namespace AgentWorkflowBuilder.Core.Engine;

public class WorkflowEngine : IWorkflowEngine
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly ICopilotSessionFactory _sessionFactory;

    public WorkflowEngine(IAgentRegistry agentRegistry, ICopilotSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(agentRegistry);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _agentRegistry = agentRegistry;
        _sessionFactory = sessionFactory;
    }

    public async Task<WorkflowExecutionEvent> ExecuteAsync(
        WorkflowDefinition workflow,
        string inputMessage,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputMessage);

        string lastOutput = string.Empty;
        await foreach (WorkflowExecutionEvent evt in ExecuteStreamingAsync(workflow, inputMessage, ct))
        {
            if (evt.EventType == ExecutionEventType.WorkflowOutput && !string.IsNullOrWhiteSpace(evt.Data))
            {
                lastOutput = evt.Data;
            }

            if (evt.EventType == ExecutionEventType.Error)
            {
                return evt;
            }
        }

        return new WorkflowExecutionEvent
        {
            EventType = ExecutionEventType.WorkflowOutput,
            Data = lastOutput
        };
    }

    public async IAsyncEnumerable<WorkflowExecutionEvent> ExecuteStreamingAsync(
        WorkflowDefinition workflow,
        string inputMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputMessage);

        List<(WorkflowNode Node, AgentDefinition Agent)> orderedAgents =
            await BuildOrderedAgentListAsync(workflow, ct);

        yield return new WorkflowExecutionEvent
        {
            EventType = ExecutionEventType.AgentStepStarted,
            Data = $"Processing {orderedAgents.Count} agent(s)…"
        };

        string currentInput = inputMessage;

        foreach ((WorkflowNode node, AgentDefinition agentDef) in orderedAgents)
        {
            ct.ThrowIfCancellationRequested();

            yield return new WorkflowExecutionEvent
            {
                EventType = ExecutionEventType.AgentStepStarted,
                NodeId = node.NodeId,
                ExecutorName = agentDef.Name,
                Data = "Agent invoked"
            };

            // Channel bridges async SDK callbacks to IAsyncEnumerable
            Channel<WorkflowExecutionEvent> channel =
                Channel.CreateUnbounded<WorkflowExecutionEvent>(
                    new UnboundedChannelOptions { SingleReader = true });

            string? finalOutput = null;
            WorkflowExecutionEvent? errorEvent = null;

            try
            {
                await using CopilotSession session = await _sessionFactory.CreateSessionAsync(
                    agentDef,
                    onEvent: evt =>
                    {
                        evt = evt with { NodeId = node.NodeId };
                        channel.Writer.TryWrite(evt);
                    },
                    ct: ct);

                // Subscribe to events via pattern matching on SessionEvent
                session.On(evt =>
                {
                    if (evt is AssistantMessageEvent msg)
                    {
                        finalOutput = msg.Data?.Content;
                    }
                    else if (evt is SessionErrorEvent error)
                    {
                        channel.Writer.TryWrite(new WorkflowExecutionEvent
                        {
                            EventType = ExecutionEventType.Error,
                            NodeId = node.NodeId,
                            ExecutorName = agentDef.Name,
                            Data = $"Agent '{agentDef.Name}' error: {error.Data?.Message}"
                        });
                    }
                });

                // Fire the prompt and wait for the AI to finish
                AssistantMessageEvent? response = await session.SendAndWaitAsync(
                    new MessageOptions { Prompt = currentInput });

                // If we didn't capture via event handler, use the response directly
                finalOutput ??= response?.Data?.Content;

                channel.Writer.Complete();
            }
            catch (OperationCanceledException)
            {
                channel.Writer.TryComplete();
                throw;
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete();
                errorEvent = new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.Error,
                    NodeId = node.NodeId,
                    ExecutorName = agentDef.Name,
                    Data = $"Agent '{agentDef.Name}' failed: {ex.Message}"
                };
            }

            // Drain streaming chunks from the channel
            await foreach (WorkflowExecutionEvent channelEvt in channel.Reader.ReadAllAsync(ct))
            {
                yield return channelEvt;
            }

            // Yield error outside catch block (C# does not allow yield in catch)
            if (errorEvent is not null)
            {
                yield return errorEvent;
                continue;
            }

            // Emit final consolidated output if we have it
            if (!string.IsNullOrWhiteSpace(finalOutput))
            {
                yield return new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.WorkflowOutput,
                    NodeId = node.NodeId,
                    ExecutorName = agentDef.Name,
                    Data = finalOutput
                };

                // Pass output as input to the next agent in the chain
                currentInput = finalOutput;
            }

            yield return new WorkflowExecutionEvent
            {
                EventType = ExecutionEventType.AgentStepCompleted,
                NodeId = node.NodeId,
                ExecutorName = agentDef.Name,
                Data = string.Empty
            };
        }
    }

    /// <summary>
    /// Resolves the workflow graph into a topologically ordered list of agent definitions.
    /// </summary>
    private async Task<List<(WorkflowNode Node, AgentDefinition Agent)>> BuildOrderedAgentListAsync(
        WorkflowDefinition definition,
        CancellationToken ct)
    {
        if (definition.Nodes.Count == 0)
            throw new InvalidOperationException("Workflow must have at least one node.");

        // Build adjacency: sourceNodeId -> list of targetNodeIds
        Dictionary<string, List<string>> adjacency = new();
        Dictionary<string, int> inDegree = new();

        foreach (WorkflowNode node in definition.Nodes)
        {
            adjacency[node.NodeId] = [];
            inDegree[node.NodeId] = 0;
        }

        foreach (WorkflowEdge edge in definition.Edges)
        {
            if (adjacency.ContainsKey(edge.SourceNodeId) &&
                inDegree.ContainsKey(edge.TargetNodeId))
            {
                adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
                inDegree[edge.TargetNodeId]++;
            }
        }

        // Topological sort (Kahn's algorithm)
        Queue<string> queue = new(
            definition.Nodes
                .Where(n => inDegree[n.NodeId] == 0)
                .Select(n => n.NodeId));

        List<string> orderedNodeIds = [];
        while (queue.Count > 0)
        {
            string nodeId = queue.Dequeue();
            orderedNodeIds.Add(nodeId);
            foreach (string neighbor in adjacency[nodeId])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        // Resolve each node to an AgentDefinition
        Dictionary<string, WorkflowNode> nodeMap = definition.Nodes.ToDictionary(n => n.NodeId);
        List<(WorkflowNode, AgentDefinition)> result = [];

        foreach (string nodeId in orderedNodeIds)
        {
            WorkflowNode node = nodeMap[nodeId];
            AgentDefinition agentDef = await _agentRegistry.GetAsync(node.AgentId, ct)
                ?? throw new InvalidOperationException($"Agent '{node.AgentId}' not found.");

            result.Add((node, agentDef));
        }

        return result;
    }
}
