using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Tests.Models;

public class ExecutionRecordTests
{
    [Fact]
    public void WhenDefaultConstructorThenHasValidDefaults()
    {
        ExecutionRecord record = new();

        Assert.NotNull(record.Id);
        Assert.NotEmpty(record.Id);
        Assert.Equal(string.Empty, record.WorkflowId);
        Assert.Equal("manual", record.TriggerType);
        Assert.Equal(ExecutionStatus.Running, record.Status);
        Assert.Equal(PauseType.None, record.PauseType);
        Assert.Null(record.CurrentNodeId);
        Assert.Empty(record.AgentOutputs);
        Assert.Null(record.AccumulatedContext);
        Assert.Null(record.CompletedAt);
        Assert.Empty(record.Events);
    }

    [Fact]
    public void WhenWithExpressionThenCreatesModifiedCopy()
    {
        ExecutionRecord original = new()
        {
            WorkflowId = "wf-1",
            Status = ExecutionStatus.Running
        };

        ExecutionRecord completed = original with
        {
            Status = ExecutionStatus.Completed,
            CompletedAt = DateTime.UtcNow
        };

        Assert.Equal(ExecutionStatus.Running, original.Status);
        Assert.Equal(ExecutionStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public void WhenPausedForClarificationThenHasCorrectState()
    {
        ExecutionRecord record = new()
        {
            Status = ExecutionStatus.Paused,
            PauseType = PauseType.Clarification,
            CurrentNodeId = "n1",
            PauseDetails = "Agent needs more context"
        };

        Assert.Equal(ExecutionStatus.Paused, record.Status);
        Assert.Equal(PauseType.Clarification, record.PauseType);
        Assert.Equal("n1", record.CurrentNodeId);
        Assert.Equal("Agent needs more context", record.PauseDetails);
    }

    [Fact]
    public void WhenAgentOutputsPopulatedThenAccessible()
    {
        ExecutionRecord record = new()
        {
            AgentOutputs = new Dictionary<string, string>
            {
                ["n1"] = "First output",
                ["n2"] = "Second output"
            }
        };

        Assert.Equal(2, record.AgentOutputs.Count);
        Assert.Equal("First output", record.AgentOutputs["n1"]);
    }

    [Fact]
    public void WhenEventsAddedThenContainsItems()
    {
        ExecutionRecord record = new()
        {
            Events =
            [
                new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.AgentStepStarted,
                    Data = "Starting"
                },
                new WorkflowExecutionEvent
                {
                    EventType = ExecutionEventType.WorkflowOutput,
                    Data = "Output"
                }
            ]
        };

        Assert.Equal(2, record.Events.Count);
        Assert.Equal(ExecutionEventType.AgentStepStarted, record.Events[0].EventType);
    }

    [Fact]
    public void WhenWorkflowExecutionEventThenHasCorrectDefaults()
    {
        WorkflowExecutionEvent evt = new()
        {
            EventType = ExecutionEventType.AgentStepStarted,
            ExecutionId = "exec-1",
            NodeId = "n1",
            ExecutorName = "Test Agent"
        };

        Assert.Equal(ExecutionEventType.AgentStepStarted, evt.EventType);
        Assert.Equal("exec-1", evt.ExecutionId);
        Assert.Equal("n1", evt.NodeId);
        Assert.Equal("Test Agent", evt.ExecutorName);
        Assert.Equal(string.Empty, evt.Data);
    }

    [Fact]
    public void WhenGateResponseApprovedThenHasEditedOutput()
    {
        GateResponse response = new()
        {
            Status = GateResponseStatus.Approved,
            EditedOutput = "Modified content"
        };

        Assert.Equal(GateResponseStatus.Approved, response.Status);
        Assert.Equal("Modified content", response.EditedOutput);
    }

    [Fact]
    public void WhenGateResponseRejectedThenHasReason()
    {
        GateResponse response = new()
        {
            Status = GateResponseStatus.Rejected,
            Reason = "Not acceptable"
        };

        Assert.Equal(GateResponseStatus.Rejected, response.Status);
        Assert.Equal("Not acceptable", response.Reason);
    }

    [Fact]
    public void WhenGateResponseSendBackThenHasFeedback()
    {
        GateResponse response = new()
        {
            Status = GateResponseStatus.SendBack,
            Feedback = "Please revise section 3"
        };

        Assert.Equal(GateResponseStatus.SendBack, response.Status);
        Assert.Equal("Please revise section 3", response.Feedback);
    }

    [Fact]
    public void WhenPlanStepInfoThenHasCorrectFields()
    {
        PlanStepInfo step = new()
        {
            StepNumber = 1,
            Title = "Write code",
            Instruction = "Implement the feature",
            AgentHint = "Developer",
            MatchedAgentId = "dev-1",
            MatchedAgentName = "Developer Agent"
        };

        Assert.Equal(1, step.StepNumber);
        Assert.Equal("Write code", step.Title);
        Assert.Equal("Implement the feature", step.Instruction);
        Assert.Equal("Developer", step.AgentHint);
        Assert.Equal("dev-1", step.MatchedAgentId);
        Assert.Equal("Developer Agent", step.MatchedAgentName);
    }

    [Fact]
    public void WhenExecutionStatusEnumThenAllValuesExist()
    {
        Assert.Equal(5, Enum.GetValues<ExecutionStatus>().Length);
        Assert.True(Enum.IsDefined(ExecutionStatus.Running));
        Assert.True(Enum.IsDefined(ExecutionStatus.Paused));
        Assert.True(Enum.IsDefined(ExecutionStatus.Completed));
        Assert.True(Enum.IsDefined(ExecutionStatus.Failed));
        Assert.True(Enum.IsDefined(ExecutionStatus.Cancelled));
    }

    [Fact]
    public void WhenPauseTypeEnumThenAllValuesExist()
    {
        Assert.Equal(4, Enum.GetValues<PauseType>().Length);
        Assert.True(Enum.IsDefined(PauseType.None));
        Assert.True(Enum.IsDefined(PauseType.Gate));
        Assert.True(Enum.IsDefined(PauseType.Clarification));
        Assert.True(Enum.IsDefined(PauseType.SendBack));
    }

    [Fact]
    public void WhenExecutionEventTypeEnumThenAllValuesExist()
    {
        Assert.Equal(12, Enum.GetValues<ExecutionEventType>().Length);
    }

    [Fact]
    public void WhenClarificationEventThenHasQuestionField()
    {
        WorkflowExecutionEvent evt = new()
        {
            EventType = ExecutionEventType.ClarificationNeeded,
            Question = "What language do you want?",
            Data = "What language do you want?"
        };

        Assert.Equal(ExecutionEventType.ClarificationNeeded, evt.EventType);
        Assert.Equal("What language do you want?", evt.Question);
    }

    [Fact]
    public void WhenGateEventThenHasGateFields()
    {
        WorkflowExecutionEvent evt = new()
        {
            EventType = ExecutionEventType.GateAwaitingApproval,
            GateType = "Approval",
            GateInstructions = "Check the output",
            PreviousAgentOutput = "Some generated code"
        };

        Assert.Equal("Approval", evt.GateType);
        Assert.Equal("Check the output", evt.GateInstructions);
        Assert.Equal("Some generated code", evt.PreviousAgentOutput);
    }

    [Fact]
    public void WhenLoopEventThenHasIterationFields()
    {
        WorkflowExecutionEvent evt = new()
        {
            EventType = ExecutionEventType.LoopIterationStarted,
            LoopIteration = 2,
            MaxIterations = 5,
            Data = "Loop iteration 2/5"
        };

        Assert.Equal(2, evt.LoopIteration);
        Assert.Equal(5, evt.MaxIterations);
    }

    [Fact]
    public void WhenPlanEventThenHasPlanSteps()
    {
        WorkflowExecutionEvent evt = new()
        {
            EventType = ExecutionEventType.PlanGenerated,
            PlanSteps =
            [
                new PlanStepInfo { StepNumber = 1, Title = "Step 1" },
                new PlanStepInfo { StepNumber = 2, Title = "Step 2" }
            ]
        };

        Assert.NotNull(evt.PlanSteps);
        Assert.Equal(2, evt.PlanSteps.Count);
    }

    [Fact]
    public void WhenWorkflowExecutionRequestThenHasDefaults()
    {
        WorkflowExecutionRequest request = new()
        {
            WorkflowId = "wf-1",
            Content = "Hello"
        };

        Assert.Equal(ExecutionInputType.Chat, request.InputType);
        Assert.Null(request.Parameters);
    }

    [Fact]
    public void WhenFileInputTypeThenSet()
    {
        WorkflowExecutionRequest request = new()
        {
            InputType = ExecutionInputType.File,
            Content = "file-data"
        };

        Assert.Equal(ExecutionInputType.File, request.InputType);
    }
}
