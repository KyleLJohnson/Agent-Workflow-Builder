using AgentWorkflowBuilder.Core.Engine;
using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Core.Tests.Engine;

public class AgentMatcherTests
{
    private readonly AgentMatcher _matcher = new();

    private static AgentDefinition CreateAgent(
        string id = "agent-1",
        string name = "Code Reviewer",
        string category = "Development",
        string description = "Reviews code for bugs and style issues") =>
        new()
        {
            Id = id,
            Name = name,
            Category = category,
            Description = description
        };

    [Fact]
    public void WhenNoAgentsThenReturnsNull()
    {
        AgentDefinition? result = _matcher.FindBestMatch("hint", "instruction", []);

        Assert.Null(result);
    }

    [Fact]
    public void WhenExactNameMatchThenReturnsAgent()
    {
        AgentDefinition agent = CreateAgent(name: "Code Reviewer");
        List<AgentDefinition> agents = [agent];

        AgentDefinition? result = _matcher.FindBestMatch("Code Reviewer", "Review some code", agents);

        Assert.NotNull(result);
        Assert.Equal("Code Reviewer", result.Name);
    }

    [Fact]
    public void WhenExactNameMatchIsCaseInsensitive()
    {
        AgentDefinition agent = CreateAgent(name: "Code Reviewer");
        List<AgentDefinition> agents = [agent];

        AgentDefinition? result = _matcher.FindBestMatch("code reviewer", "instruction", agents);

        Assert.NotNull(result);
        Assert.Equal("Code Reviewer", result.Name);
    }

    [Fact]
    public void WhenHintContainsNameThenScoresPartialMatch()
    {
        AgentDefinition agent = CreateAgent(name: "Reviewer");
        AgentDefinition other = CreateAgent(id: "agent-2", name: "Logger", category: "Ops", description: "Logs things");
        List<AgentDefinition> agents = [agent, other];

        AgentDefinition? result = _matcher.FindBestMatch("Code Reviewer Agent", "instruction", agents);

        Assert.NotNull(result);
        Assert.Equal("Reviewer", result.Name);
    }

    [Fact]
    public void WhenNameContainsHintThenScoresPartialMatch()
    {
        AgentDefinition agent = CreateAgent(name: "Advanced Code Reviewer");
        AgentDefinition other = CreateAgent(id: "agent-2", name: "Logger", category: "Ops", description: "Logs things");
        List<AgentDefinition> agents = [agent, other];

        AgentDefinition? result = _matcher.FindBestMatch("Code", "instruction", agents);

        Assert.NotNull(result);
        Assert.Equal("Advanced Code Reviewer", result.Name);
    }

    [Fact]
    public void WhenCategoryMatchesThenBoostsScore()
    {
        AgentDefinition devAgent = CreateAgent(name: "Alpha", category: "Development");
        AgentDefinition opsAgent = CreateAgent(id: "agent-2", name: "Beta", category: "Operations", description: "Handles ops");
        List<AgentDefinition> agents = [devAgent, opsAgent];

        AgentDefinition? result = _matcher.FindBestMatch("Development", "build something", agents);

        Assert.NotNull(result);
        Assert.Equal("Alpha", result.Name);
    }

    [Fact]
    public void WhenDescriptionKeywordsOverlapThenBoostsScore()
    {
        AgentDefinition agent = CreateAgent(
            name: "Analyzer",
            description: "Analyzes performance bottlenecks and memory leaks in code");
        AgentDefinition other = CreateAgent(
            id: "agent-2",
            name: "Writer",
            category: "Content",
            description: "Writes blog posts");
        List<AgentDefinition> agents = [agent, other];

        // "performance" and "memory" are >3 chars and appear in description
        AgentDefinition? result = _matcher.FindBestMatch(
            "unknown-hint", "check performance and memory usage", agents);

        Assert.NotNull(result);
        Assert.Equal("Analyzer", result.Name);
    }

    [Fact]
    public void WhenShortWordsInInstructionThenIgnored()
    {
        AgentDefinition agent = CreateAgent(description: "A tool for and the with");
        List<AgentDefinition> agents = [agent];

        // Words 3 chars or shorter should not score
        AgentDefinition? result = _matcher.FindBestMatch("no-match", "a to for and the", agents);

        // No score from description keywords means no match above 0
        Assert.Null(result);
    }

    [Fact]
    public void WhenExactNameMatchBeatsCategoryMatch()
    {
        AgentDefinition exactMatch = CreateAgent(
            id: "exact",
            name: "Translator",
            category: "Language",
            description: "Translates text");
        AgentDefinition categoryMatch = CreateAgent(
            id: "category",
            name: "Other Agent",
            category: "Translator",
            description: "Something else for translation work");
        List<AgentDefinition> agents = [exactMatch, categoryMatch];

        AgentDefinition? result = _matcher.FindBestMatch("Translator", "translate this text", agents);

        Assert.NotNull(result);
        Assert.Equal("exact", result.Id);
    }

    [Fact]
    public void WhenIdContainsHintThenScores()
    {
        AgentDefinition agent = CreateAgent(
            id: "code-review-v2",
            name: "ReviewBot",
            category: "QA",
            description: "Quality assurance bot");
        AgentDefinition other = CreateAgent(
            id: "logger-v1",
            name: "LogBot",
            category: "Ops",
            description: "Logging bot");
        List<AgentDefinition> agents = [agent, other];

        AgentDefinition? result = _matcher.FindBestMatch("code-review", "do a review", agents);

        Assert.NotNull(result);
        Assert.Equal("code-review-v2", result.Id);
    }

    [Fact]
    public void WhenMultipleAgentsScoreEquallyThenReturnsFirst()
    {
        AgentDefinition agent1 = CreateAgent(id: "a1", name: "Agent", category: "Cat");
        AgentDefinition agent2 = CreateAgent(id: "a2", name: "Agent", category: "Cat");
        List<AgentDefinition> agents = [agent1, agent2];

        AgentDefinition? result = _matcher.FindBestMatch("Agent", "do something", agents);

        Assert.NotNull(result);
        Assert.Equal("a1", result.Id);
    }

    [Fact]
    public void WhenEmptyDescriptionThenNoKeywordScoring()
    {
        AgentDefinition agent = CreateAgent(description: "");
        List<AgentDefinition> agents = [agent];

        // With no name/category match and empty description, score stays 0
        AgentDefinition? result = _matcher.FindBestMatch("unrelated-hint", "check performance and memory", agents);

        Assert.Null(result);
    }
}
