using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Tests.Models;

public class WorkflowDefinitionTests
{
    [Fact]
    public void WhenDefaultConstructorThenHasValidDefaults()
    {
        WorkflowDefinition workflow = new();

        Assert.NotNull(workflow.Id);
        Assert.NotEmpty(workflow.Id);
        Assert.Equal("New Workflow", workflow.Name);
        Assert.Empty(workflow.Nodes);
        Assert.Empty(workflow.Edges);
        Assert.Null(workflow.UserId);
        Assert.Null(workflow.BlobContainerName);
    }

    [Fact]
    public void WhenWithExpressionThenCreatesModifiedCopy()
    {
        WorkflowDefinition original = new() { Name = "Original" };

        WorkflowDefinition modified = original with { Name = "Modified" };

        Assert.Equal("Original", original.Name);
        Assert.Equal("Modified", modified.Name);
        Assert.Equal(original.Id, modified.Id);
    }

    [Fact]
    public void WhenNodesAddedThenNodeListContainsItems()
    {
        WorkflowDefinition workflow = new()
        {
            Nodes =
            [
                new WorkflowNode { NodeId = "n1", AgentId = "a1", Label = "Agent 1" },
                new WorkflowNode { NodeId = "n2", AgentId = "a2", Label = "Agent 2" }
            ]
        };

        Assert.Equal(2, workflow.Nodes.Count);
        Assert.Equal("n1", workflow.Nodes[0].NodeId);
        Assert.Equal("n2", workflow.Nodes[1].NodeId);
    }

    [Fact]
    public void WhenEdgeCreatedThenHasDefaults()
    {
        WorkflowEdge edge = new()
        {
            SourceNodeId = "n1",
            TargetNodeId = "n2"
        };

        Assert.NotEmpty(edge.Id);
        Assert.Equal("n1", edge.SourceNodeId);
        Assert.Equal("n2", edge.TargetNodeId);
        Assert.False(edge.IsBackEdge);
        Assert.Null(edge.MaxIterations);
    }

    [Fact]
    public void WhenBackEdgeThenFlagIsSet()
    {
        WorkflowEdge edge = new()
        {
            SourceNodeId = "n2",
            TargetNodeId = "n1",
            IsBackEdge = true,
            MaxIterations = 5
        };

        Assert.True(edge.IsBackEdge);
        Assert.Equal(5, edge.MaxIterations);
    }

    [Fact]
    public void WhenGateNodeThenHasCorrectNodeType()
    {
        WorkflowNode gate = new()
        {
            NodeId = "gate-1",
            NodeType = "gate",
            GateConfig = new GateConfiguration
            {
                GateType = GateType.Approval,
                Instructions = "Review and approve"
            }
        };

        Assert.Equal("gate", gate.NodeType);
        Assert.NotNull(gate.GateConfig);
        Assert.Equal(GateType.Approval, gate.GateConfig.GateType);
        Assert.Equal("Review and approve", gate.GateConfig.Instructions);
    }

    [Fact]
    public void WhenAgentNodeThenDefaultNodeTypeIsAgent()
    {
        WorkflowNode node = new();

        Assert.Equal("agent", node.NodeType);
    }

    [Fact]
    public void WhenConfigOverridesSetThenAccessible()
    {
        WorkflowNode node = new()
        {
            ConfigOverrides = new Dictionary<string, string>
            {
                ["temperature"] = "0.8",
                ["model"] = "gpt-4o"
            }
        };

        Assert.NotNull(node.ConfigOverrides);
        Assert.Equal("0.8", node.ConfigOverrides["temperature"]);
    }

    [Fact]
    public void WhenGateConfigWithSendBackTargetThenSet()
    {
        GateConfiguration config = new()
        {
            GateType = GateType.ReviewAndEdit,
            SendBackTargetNodeId = "n1"
        };

        Assert.Equal(GateType.ReviewAndEdit, config.GateType);
        Assert.Equal("n1", config.SendBackTargetNodeId);
    }
}
