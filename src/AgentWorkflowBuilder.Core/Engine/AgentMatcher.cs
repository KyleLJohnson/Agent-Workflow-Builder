namespace AgentWorkflowBuilder.Core.Engine;

using AgentWorkflowBuilder.Core.Models;

/// <summary>
/// Scores and matches planner step hints against the agent registry.
/// </summary>
internal class AgentMatcher
{
    /// <summary>
    /// Finds the best matching agent based on the planner's hint and instruction text.
    /// </summary>
    internal AgentDefinition? FindBestMatch(
        string agentHint,
        string instruction,
        IReadOnlyList<AgentDefinition> agents)
    {
        if (agents.Count == 0)
            return null;

        AgentDefinition? bestAgent = null;
        int bestScore = 0;

        foreach (AgentDefinition agent in agents)
        {
            int score = ScoreAgent(agent, agentHint, instruction);
            if (score > bestScore)
            {
                bestScore = score;
                bestAgent = agent;
            }
        }

        return bestAgent;
    }

    private static int ScoreAgent(AgentDefinition agent, string hint, string instruction)
    {
        int score = 0;

        // Exact name match (case-insensitive)
        if (agent.Name.Equals(hint, StringComparison.OrdinalIgnoreCase))
            score += 100;

        // Name contains hint or hint contains name
        if (agent.Name.Contains(hint, StringComparison.OrdinalIgnoreCase) ||
            hint.Contains(agent.Name, StringComparison.OrdinalIgnoreCase))
            score += 50;

        // Category match
        if (!string.IsNullOrWhiteSpace(agent.Category) &&
            hint.Contains(agent.Category, StringComparison.OrdinalIgnoreCase))
            score += 40;

        // ID contains hint
        if (agent.Id.Contains(hint, StringComparison.OrdinalIgnoreCase))
            score += 30;

        // Description keyword overlap with instruction
        if (!string.IsNullOrWhiteSpace(agent.Description))
        {
            string[] instructionWords = instruction.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (string word in instructionWords)
            {
                if (word.Length > 3 && agent.Description.Contains(word, StringComparison.OrdinalIgnoreCase))
                    score += 5;
            }
        }

        return score;
    }
}
