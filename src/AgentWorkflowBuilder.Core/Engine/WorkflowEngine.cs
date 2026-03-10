using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentWorkflowBuilder.Core.Engine;

public class WorkflowEngine : IWorkflowEngine
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly ICopilotSessionFactory _sessionFactory;
    private readonly ExecutionSessionManager _sessionManager;
    private readonly IExecutionStore _executionStore;
    private readonly ILogger<WorkflowEngine> _logger;
    private readonly int _maxLoopIterations;

    public WorkflowEngine(
        IAgentRegistry agentRegistry,
        ICopilotSessionFactory sessionFactory,
        ExecutionSessionManager sessionManager,
        IExecutionStore executionStore,
        IConfiguration configuration,
        ILogger<WorkflowEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(agentRegistry);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(executionStore);
        ArgumentNullException.ThrowIfNull(logger);
        _agentRegistry = agentRegistry;
        _sessionFactory = sessionFactory;
        _sessionManager = sessionManager;
        _executionStore = executionStore;
        _logger = logger;
        _maxLoopIterations = int.TryParse(
            configuration?["Workflow:MaxLoopIterations"], out int val) ? val : 3;
    }

    public async Task<WorkflowExecutionEvent> ExecuteAsync(
        WorkflowDefinition workflow,
        string inputMessage,
        bool autoApproveGates = false,
        string? executionId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputMessage);

        string lastOutput = string.Empty;
        await foreach (WorkflowExecutionEvent evt in ExecuteStreamingAsync(workflow, inputMessage, autoApproveGates, executionId, ct))
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
        bool autoApproveGates = false,
        string? executionId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputMessage);

        executionId ??= Guid.NewGuid().ToString();
        ExecutionSession session = _sessionManager.CreateSession(executionId);
        session.WorkflowId = workflow.Id;

        // Create initial execution record
        ExecutionRecord executionRecord = new()
        {
            Id = executionId,
            WorkflowId = workflow.Id,
            Status = ExecutionStatus.Running,
            StartedAt = DateTime.UtcNow
        };
        await _executionStore.SaveAsync(executionRecord, ct);

        try
        {
            ExecutionPlan plan = await BuildExecutionPlanAsync(workflow, ct);

            yield return new WorkflowExecutionEvent
            {
                EventType = ExecutionEventType.AgentStepStarted,
                ExecutionId = executionId,
                Data = $"Processing {plan.Steps.Count} step(s)…"
            };

            string currentInput = inputMessage;

            foreach (ExecutionStep step in plan.Steps)
            {
                ct.ThrowIfCancellationRequested();

                if (step is SingleNodeStep singleStep)
                {
                    if (singleStep.Node.NodeType == "gate")
                    {
                        await foreach (WorkflowExecutionEvent gateEvt in HandleGateNodeAsync(
                            singleStep.Node, currentInput, executionId, autoApproveGates, ct))
                        {
                            yield return gateEvt;

                            if (gateEvt.EventType == ExecutionEventType.GateRejected)
                            {
                                yield break;
                            }

                            if (gateEvt.EventType == ExecutionEventType.GateApproved &&
                                !string.IsNullOrWhiteSpace(gateEvt.Data))
                            {
                                currentInput = gateEvt.Data;
                            }

                            if (gateEvt.EventType == ExecutionEventType.GateAutoApproved &&
                                !string.IsNullOrWhiteSpace(gateEvt.Data))
                            {
                                currentInput = gateEvt.Data;
                            }
                        }
                    }
                    else
                    {
                        await foreach (WorkflowExecutionEvent agentEvt in RunAgentNodeAsync(
                            singleStep.Node, singleStep.Agent!, currentInput, executionId, plan, ct))
                        {
                            yield return agentEvt;

                            if (agentEvt.EventType == ExecutionEventType.WorkflowOutput &&
                                !string.IsNullOrWhiteSpace(agentEvt.Data))
                            {
                                currentInput = agentEvt.Data;
                            }
                        }
                    }
                }
                else if (step is LoopGroupStep loopStep)
                {
                    await foreach (WorkflowExecutionEvent loopEvt in RunLoopAsync(
                        loopStep, currentInput, executionId, plan, autoApproveGates, ct))
                    {
                        yield return loopEvt;

                        if (loopEvt.EventType == ExecutionEventType.WorkflowOutput &&
                            !string.IsNullOrWhiteSpace(loopEvt.Data))
                        {
                            currentInput = loopEvt.Data;
                        }
                    }
                }
            }

            yield return new WorkflowExecutionEvent
            {
                EventType = ExecutionEventType.AgentStepCompleted,
                ExecutionId = executionId,
                Data = "Workflow execution completed"
            };

            // Save completed state
            await _executionStore.SaveAsync(executionRecord with
            {
                Status = ExecutionStatus.Completed,
                CompletedAt = DateTime.UtcNow,
                AgentOutputs = new Dictionary<string, string>(session.AgentOutputs),
                AccumulatedContext = currentInput
            }, ct);
        }
        finally
        {
            _sessionManager.RemoveSession(executionId);
        }
    }

    // ──────────────────────────────────────────────────────────
    // Agent node execution
    // ──────────────────────────────────────────────────────────

    private async IAsyncEnumerable<WorkflowExecutionEvent> RunAgentNodeAsync(
        WorkflowNode node,
        AgentDefinition agentDef,
        string input,
        string executionId,
        ExecutionPlan plan,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new WorkflowExecutionEvent
        {
            EventType = ExecutionEventType.AgentStepStarted,
            ExecutionId = executionId,
            NodeId = node.NodeId,
            ExecutorName = agentDef.Name,
            Data = "Agent invoked"
        };

        Channel<WorkflowExecutionEvent> channel =
            Channel.CreateUnbounded<WorkflowExecutionEvent>(
                new UnboundedChannelOptions { SingleReader = true });

        string? finalOutput = null;
        WorkflowExecutionEvent? errorEvent = null;

        try
        {
            Func<UserInputRequest, UserInputInvocation, Task<UserInputResponse>>? onUserInput = null;
            if (agentDef.AllowClarification)
            {
                onUserInput = async (request, invocation) =>
                {
                    channel.Writer.TryWrite(new WorkflowExecutionEvent
                    {
                        EventType = ExecutionEventType.ClarificationNeeded,
                        ExecutionId = executionId,
                        NodeId = node.NodeId,
                        ExecutorName = agentDef.Name,
                        Question = request.Question,
                        Data = request.Question ?? "The agent needs more information."
                    });

                    try
                    {
                        string answer = await _sessionManager.WaitForClarificationAsync(executionId, ct);
                        return new UserInputResponse { Answer = answer };
                    }
                    catch (TimeoutException)
                    {
                        channel.Writer.TryWrite(new WorkflowExecutionEvent
                        {
                            EventType = ExecutionEventType.Error,
                            ExecutionId = executionId,
                            NodeId = node.NodeId,
                            ExecutorName = agentDef.Name,
                            Data = "Clarification timed out."
                        });
                        return new UserInputResponse { Answer = "No response provided (timed out)." };
                    }
                };
            }

            await using CopilotSession session = await _sessionFactory.CreateSessionAsync(
                agentDef,
                onEvent: evt =>
                {
                    evt = evt with { NodeId = node.NodeId, ExecutionId = executionId };
                    channel.Writer.TryWrite(evt);
                },
                onUserInput: onUserInput,
                ct: ct);

            // Cancel SendAndWaitAsync if the SDK reports a session error
            using CancellationTokenSource errorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

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
                        ExecutionId = executionId,
                        NodeId = node.NodeId,
                        ExecutorName = agentDef.Name,
                        Data = $"Agent '{agentDef.Name}' error: {error.Data?.Message}"
                    });
                    // Abort the pending SendAndWaitAsync so it doesn't hang
                    errorCts.Cancel();
                }
            });

            try
            {
                AssistantMessageEvent? response = await session.SendAndWaitAsync(
                    new MessageOptions { Prompt = input }).WaitAsync(errorCts.Token);

                finalOutput ??= response?.Data?.Content;
            }
            catch (OperationCanceledException) when (errorCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // SessionErrorEvent fired — error already written to channel
            }

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
                ExecutionId = executionId,
                NodeId = node.NodeId,
                ExecutorName = agentDef.Name,
                Data = $"Agent '{agentDef.Name}' failed: {ex.Message}"
            };
        }

        await foreach (WorkflowExecutionEvent channelEvt in channel.Reader.ReadAllAsync(ct))
        {
            yield return channelEvt;
        }

        if (errorEvent is not null)
        {
            yield return errorEvent;
            yield break;
        }

        // When the agent's output appears to be asking the user a question rather than
        // producing a substantive result, pause for a user answer and re-run the agent.
        if (agentDef.AllowClarification
            && !string.IsNullOrWhiteSpace(finalOutput)
            && LooksLikeQuestion(finalOutput))
        {
            yield return new WorkflowExecutionEvent
            {
                EventType = ExecutionEventType.ClarificationNeeded,
                ExecutionId = executionId,
                NodeId = node.NodeId,
                ExecutorName = agentDef.Name,
                Question = finalOutput,
                Data = finalOutput
            };

            string? clarificationAnswer = null;
            bool clarificationTimedOut = false;
            try
            {
                clarificationAnswer = await _sessionManager.WaitForClarificationAsync(executionId, ct);
            }
            catch (TimeoutException)
            {
                clarificationTimedOut = true;
            }

            if (clarificationTimedOut)
            {
                yield return new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.Error,
                    ExecutionId = executionId,
                    NodeId = node.NodeId,
                    ExecutorName = agentDef.Name,
                    Data = "Clarification timed out — continuing with the agent's original output."
                };
            }
            else if (!string.IsNullOrWhiteSpace(clarificationAnswer))
            {
                // Combine original input, agent's question, and user's answer
                // so the agent retains full context on the follow-up.
                string combinedInput = $"{input}\n\nAgent asked: {finalOutput}\nUser answered: {clarificationAnswer}";

                // Re-run the agent with full context
                await foreach (WorkflowExecutionEvent followUp in RunAgentNodeAsync(
                    node, agentDef, combinedInput, executionId, plan, ct))
                {
                    yield return followUp;
                }

                yield break;
            }
        }

        // Check for planner output
        if (agentDef.AgentType == "planner" && !string.IsNullOrWhiteSpace(finalOutput))
        {
            List<PlanStepInfo>? planSteps = ParsePlanOutput(finalOutput);
            if (planSteps is { Count: > 0 })
            {
                bool hasDownstream = plan.Definition.Edges.Any(e =>
                    e.SourceNodeId == node.NodeId && !e.IsBackEdge);

                if (!hasDownstream)
                {
                    IReadOnlyList<AgentDefinition> allAgents = await _agentRegistry.ListAsync(ct);
                    AgentMatcher matcher = new();
                    List<PlanStepInfo> resolvedSteps = [];
                    foreach (PlanStepInfo ps in planSteps)
                    {
                        AgentDefinition? matched = matcher.FindBestMatch(
                            ps.AgentHint, ps.Instruction, allAgents);
                        resolvedSteps.Add(ps with
                        {
                            MatchedAgentId = matched?.Id,
                            MatchedAgentName = matched?.Name
                        });
                    }

                    yield return new WorkflowExecutionEvent
                    {
                        EventType = ExecutionEventType.PlanGenerated,
                        ExecutionId = executionId,
                        NodeId = node.NodeId,
                        ExecutorName = agentDef.Name,
                        PlanSteps = resolvedSteps,
                        Data = $"Generated plan with {resolvedSteps.Count} steps"
                    };

                    string planInput = finalOutput;
                    foreach (PlanStepInfo planStep in resolvedSteps)
                    {
                        if (string.IsNullOrWhiteSpace(planStep.MatchedAgentId))
                            continue;

                        AgentDefinition? stepAgent = await _agentRegistry.GetAsync(planStep.MatchedAgentId, ct);
                        if (stepAgent is null) continue;

                        WorkflowNode virtualNode = new()
                        {
                            NodeId = $"plan-step-{planStep.StepNumber}",
                            AgentId = stepAgent.Id,
                            Label = planStep.Title
                        };

                        string stepInput = $"Step {planStep.StepNumber}: {planStep.Instruction}\n\nContext:\n{planInput}";

                        await foreach (WorkflowExecutionEvent stepEvt in RunAgentNodeAsync(
                            virtualNode, stepAgent, stepInput, executionId, plan, ct))
                        {
                            yield return stepEvt;
                            if (stepEvt.EventType == ExecutionEventType.WorkflowOutput &&
                                !string.IsNullOrWhiteSpace(stepEvt.Data))
                            {
                                planInput = stepEvt.Data;
                            }
                        }
                    }

                    finalOutput = planInput;
                }
                else
                {
                    yield return new WorkflowExecutionEvent
                    {
                        EventType = ExecutionEventType.PlanGenerated,
                        ExecutionId = executionId,
                        NodeId = node.NodeId,
                        ExecutorName = agentDef.Name,
                        PlanSteps = planSteps,
                        Data = $"Generated plan with {planSteps.Count} steps (routing to connected agents)"
                    };
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(finalOutput))
        {
            // Track agent output on the session for checkpointing
            if (_sessionManager.TryGetSession(executionId, out ExecutionSession? execSession))
            {
                execSession.AgentOutputs[node.NodeId] = finalOutput;
                execSession.CurrentNodeId = node.NodeId;
                execSession.AccumulatedContext = finalOutput;
            }

            yield return new WorkflowExecutionEvent
            {
                EventType = ExecutionEventType.WorkflowOutput,
                ExecutionId = executionId,
                NodeId = node.NodeId,
                ExecutorName = agentDef.Name,
                Data = finalOutput
            };
        }

        yield return new WorkflowExecutionEvent
        {
            EventType = ExecutionEventType.AgentStepCompleted,
            ExecutionId = executionId,
            NodeId = node.NodeId,
            ExecutorName = agentDef.Name,
            Data = string.Empty
        };
    }

    // ──────────────────────────────────────────────────────────
    // Gate node handling
    // ──────────────────────────────────────────────────────────

    private async IAsyncEnumerable<WorkflowExecutionEvent> HandleGateNodeAsync(
        WorkflowNode gateNode,
        string previousOutput,
        string executionId,
        bool autoApproveGates,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string gateType = gateNode.GateConfig?.GateType.ToString() ?? "Approval";

        if (autoApproveGates)
        {
            _logger.LogInformation("Auto-approving gate {NodeId} in execution {ExecutionId}", gateNode.NodeId, executionId);
            yield return new WorkflowExecutionEvent
            {
                EventType = ExecutionEventType.GateAutoApproved,
                ExecutionId = executionId,
                NodeId = gateNode.NodeId,
                PreviousAgentOutput = previousOutput,
                GateType = gateType,
                GateInstructions = gateNode.GateConfig?.Instructions,
                Data = previousOutput
            };
            yield break;
        }

        yield return new WorkflowExecutionEvent
        {
            EventType = ExecutionEventType.GateAwaitingApproval,
            ExecutionId = executionId,
            NodeId = gateNode.NodeId,
            PreviousAgentOutput = previousOutput,
            GateType = gateType,
            GateInstructions = gateNode.GateConfig?.Instructions,
            Data = $"Gate awaiting {gateType.ToLowerInvariant()}"
        };

        GateResponse response;
        WorkflowExecutionEvent? timeoutEvent = null;
        try
        {
            response = await _sessionManager.WaitForGateResponseAsync(executionId, ct);
        }
        catch (TimeoutException)
        {
            timeoutEvent = new WorkflowExecutionEvent
            {
                EventType = ExecutionEventType.Error,
                ExecutionId = executionId,
                NodeId = gateNode.NodeId,
                Data = "Gate approval timed out."
            };
            response = new GateResponse { Status = GateResponseStatus.Rejected, Reason = "Timed out" };
        }

        if (timeoutEvent is not null)
        {
            yield return timeoutEvent;
            yield break;
        }

        switch (response.Status)
        {
            case GateResponseStatus.Approved:
                string approvedOutput = response.EditedOutput ?? previousOutput;
                yield return new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.GateApproved,
                    ExecutionId = executionId,
                    NodeId = gateNode.NodeId,
                    Data = approvedOutput
                };
                break;

            case GateResponseStatus.Rejected:
                yield return new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.GateRejected,
                    ExecutionId = executionId,
                    NodeId = gateNode.NodeId,
                    Data = response.Reason ?? "Rejected by reviewer."
                };
                break;

            case GateResponseStatus.SendBack:
                yield return new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.GateRejected,
                    ExecutionId = executionId,
                    NodeId = gateNode.NodeId,
                    Data = response.Feedback ?? "Sent back for revision."
                };
                break;
        }
    }

    // ──────────────────────────────────────────────────────────
    // Loop execution
    // ──────────────────────────────────────────────────────────

    private async IAsyncEnumerable<WorkflowExecutionEvent> RunLoopAsync(
        LoopGroupStep loopStep,
        string entryInput,
        string executionId,
        ExecutionPlan plan,
        bool autoApproveGates,
        [EnumeratorCancellation] CancellationToken ct)
    {
        int maxIterations = loopStep.MaxIterations ?? _maxLoopIterations;
        string currentInput = entryInput;

        for (int iteration = 1; iteration <= maxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            yield return new WorkflowExecutionEvent
            {
                EventType = ExecutionEventType.LoopIterationStarted,
                ExecutionId = executionId,
                LoopIteration = iteration,
                MaxIterations = maxIterations,
                Data = $"Loop iteration {iteration}/{maxIterations}"
            };

            string iterationOutput = currentInput;

            foreach ((WorkflowNode node, AgentDefinition? agent) in loopStep.Nodes)
            {
                ct.ThrowIfCancellationRequested();

                if (node.NodeType == "gate")
                {
                    bool aborted = false;
                    await foreach (WorkflowExecutionEvent gateEvt in HandleGateNodeAsync(
                        node, iterationOutput, executionId, autoApproveGates, ct))
                    {
                        yield return gateEvt;

                        if (gateEvt.EventType == ExecutionEventType.GateRejected)
                        {
                            aborted = true;
                            break;
                        }

                        if (gateEvt.EventType == ExecutionEventType.GateApproved &&
                            !string.IsNullOrWhiteSpace(gateEvt.Data))
                        {
                            iterationOutput = gateEvt.Data;
                        }

                        if (gateEvt.EventType == ExecutionEventType.GateAutoApproved &&
                            !string.IsNullOrWhiteSpace(gateEvt.Data))
                        {
                            iterationOutput = gateEvt.Data;
                        }
                    }

                    if (aborted)
                    {
                        yield return new WorkflowExecutionEvent
                        {
                            EventType = ExecutionEventType.LoopIterationCompleted,
                            ExecutionId = executionId,
                            LoopIteration = iteration,
                            Data = "Loop aborted at gate"
                        };
                        yield break;
                    }
                }
                else if (agent is not null)
                {
                    await foreach (WorkflowExecutionEvent agentEvt in RunAgentNodeAsync(
                        node, agent, iterationOutput, executionId, plan, ct))
                    {
                        yield return agentEvt;

                        if (agentEvt.EventType == ExecutionEventType.WorkflowOutput &&
                            !string.IsNullOrWhiteSpace(agentEvt.Data))
                        {
                            iterationOutput = agentEvt.Data;
                        }
                    }
                }
            }

            yield return new WorkflowExecutionEvent
            {
                EventType = ExecutionEventType.LoopIterationCompleted,
                ExecutionId = executionId,
                LoopIteration = iteration,
                Data = $"Loop iteration {iteration} completed"
            };

            // Check for exit marker
            if (iterationOutput.Contains("<<<APPROVED>>>", StringComparison.Ordinal) ||
                iterationOutput.Contains("<<<LOOP_EXIT>>>", StringComparison.Ordinal))
            {
                currentInput = iterationOutput
                    .Replace("<<<APPROVED>>>", "", StringComparison.Ordinal)
                    .Replace("<<<LOOP_EXIT>>>", "", StringComparison.Ordinal)
                    .Trim();
                break;
            }

            currentInput = $"Previous output:\n{iterationOutput}\n\nOriginal input:\n{entryInput}\n\nPlease revise based on the feedback above.";
        }
    }

    // ──────────────────────────────────────────────────────────
    // Plan parsing
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Detects whether agent output is asking the user for more information
    /// rather than providing a substantive answer.
    /// </summary>
    private static bool LooksLikeQuestion(string output)
    {
        string trimmed = output.TrimEnd();

        // Ends with a question mark — most reliable signal
        if (trimmed.EndsWith('?'))
            return true;

        // Check the last meaningful sentence/line for request patterns
        string lastLine = trimmed.Split('\n')[^1].Trim();
        if (QuestionPatternRegex.IsMatch(lastLine))
            return true;

        return false;
    }

    private static readonly Regex QuestionPatternRegex = new(
        @"(?i)^(please\s+(provide|specify|clarify|share|describe|tell|let\s+me\s+know)|could\s+you|can\s+you|would\s+you|what\s+(would|do|is|are|kind|type)|do\s+you\s+(want|need|prefer)|let\s+me\s+know|i\s+need\s+(more|additional)\s+(info|information|details|context))",
        RegexOptions.Compiled);

    private static readonly Regex PlanBlockRegex = new(
        @"<<<PLAN>>>(.*?)<<<END_PLAN>>>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex PlanStepRegex = new(
        @"^\s*(\d+)\.\s*\[([^\]]+)\]\s*\|\s*\[agent_hint:\s*([^\]]+)\]\s*:\s*(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static List<PlanStepInfo>? ParsePlanOutput(string output)
    {
        Match blockMatch = PlanBlockRegex.Match(output);
        if (!blockMatch.Success)
            return null;

        string planContent = blockMatch.Groups[1].Value;
        MatchCollection stepMatches = PlanStepRegex.Matches(planContent);

        if (stepMatches.Count == 0)
            return null;

        List<PlanStepInfo> steps = [];
        foreach (Match m in stepMatches)
        {
            steps.Add(new PlanStepInfo
            {
                StepNumber = int.Parse(m.Groups[1].Value),
                Title = m.Groups[2].Value.Trim(),
                AgentHint = m.Groups[3].Value.Trim(),
                Instruction = m.Groups[4].Value.Trim()
            });
        }

        return steps;
    }

    // ──────────────────────────────────────────────────────────
    // Execution plan building (DAG + cycle detection)
    // ──────────────────────────────────────────────────────────

    private async Task<ExecutionPlan> BuildExecutionPlanAsync(
        WorkflowDefinition definition,
        CancellationToken ct)
    {
        if (definition.Nodes.Count == 0)
            throw new InvalidOperationException("Workflow must have at least one node.");

        List<WorkflowEdge> forwardEdges = [];
        List<WorkflowEdge> backEdges = [];
        foreach (WorkflowEdge edge in definition.Edges)
        {
            if (edge.IsBackEdge)
                backEdges.Add(edge);
            else
                forwardEdges.Add(edge);
        }

        Dictionary<string, List<string>> adjacency = new();
        Dictionary<string, int> inDegree = new();

        foreach (WorkflowNode node in definition.Nodes)
        {
            adjacency[node.NodeId] = [];
            inDegree[node.NodeId] = 0;
        }

        foreach (WorkflowEdge edge in forwardEdges)
        {
            if (adjacency.ContainsKey(edge.SourceNodeId) &&
                inDegree.ContainsKey(edge.TargetNodeId))
            {
                adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
                inDegree[edge.TargetNodeId]++;
            }
        }

        Queue<string> queue = new(
            definition.Nodes
                .Where(n => inDegree[n.NodeId] == 0)
                .Select(n => n.NodeId));

        List<string> sorted = [];
        while (queue.Count > 0)
        {
            string nodeId = queue.Dequeue();
            sorted.Add(nodeId);
            foreach (string neighbor in adjacency[nodeId])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        Dictionary<string, WorkflowNode> nodeMap = definition.Nodes.ToDictionary(n => n.NodeId);
        HashSet<string> loopNodeIds = new();
        Dictionary<string, int> loopMaxIterations = new();

        foreach (WorkflowEdge backEdge in backEdges)
        {
            int targetIdx = sorted.IndexOf(backEdge.TargetNodeId);
            int sourceIdx = sorted.IndexOf(backEdge.SourceNodeId);
            if (targetIdx < 0 || sourceIdx < 0 || targetIdx > sourceIdx)
                continue;

            for (int i = targetIdx; i <= sourceIdx; i++)
            {
                loopNodeIds.Add(sorted[i]);
            }

            if (backEdge.MaxIterations.HasValue)
            {
                loopMaxIterations[backEdge.TargetNodeId] = backEdge.MaxIterations.Value;
            }
        }

        List<ExecutionStep> steps = [];
        int idx = 0;
        while (idx < sorted.Count)
        {
            string nodeId = sorted[idx];

            if (loopNodeIds.Contains(nodeId))
            {
                List<(WorkflowNode, AgentDefinition?)> loopNodes = [];
                int loopStart = idx;
                while (idx < sorted.Count && loopNodeIds.Contains(sorted[idx]))
                {
                    WorkflowNode n = nodeMap[sorted[idx]];
                    AgentDefinition? agent = null;
                    if (n.NodeType != "gate")
                    {
                        agent = await _agentRegistry.GetAsync(n.AgentId, ct);
                    }
                    loopNodes.Add((n, agent));
                    idx++;
                }

                int? maxIter = loopMaxIterations.TryGetValue(sorted[loopStart], out int mi) ? mi : null;
                steps.Add(new LoopGroupStep
                {
                    Nodes = loopNodes,
                    MaxIterations = maxIter
                });
            }
            else
            {
                WorkflowNode node = nodeMap[nodeId];
                AgentDefinition? agent = null;
                if (node.NodeType != "gate")
                {
                    agent = await _agentRegistry.GetAsync(node.AgentId, ct)
                        ?? throw new InvalidOperationException($"Agent '{node.AgentId}' not found.");
                }
                steps.Add(new SingleNodeStep { Node = node, Agent = agent });
                idx++;
            }
        }

        return new ExecutionPlan { Steps = steps, Definition = definition };
    }
}

// ──────────────────────────────────────────────────────────
// Execution plan types
// ──────────────────────────────────────────────────────────

internal class ExecutionPlan
{
    public List<ExecutionStep> Steps { get; init; } = [];
    public WorkflowDefinition Definition { get; init; } = null!;
}

internal abstract class ExecutionStep;

internal class SingleNodeStep : ExecutionStep
{
    public WorkflowNode Node { get; init; } = null!;
    public AgentDefinition? Agent { get; init; }
}

internal class LoopGroupStep : ExecutionStep
{
    public List<(WorkflowNode Node, AgentDefinition? Agent)> Nodes { get; init; } = [];
    public int? MaxIterations { get; init; }
}
