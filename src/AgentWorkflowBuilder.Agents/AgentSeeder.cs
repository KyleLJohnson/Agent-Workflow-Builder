using System.Text.Json;
using AgentWorkflowBuilder.Core.Models;

namespace AgentWorkflowBuilder.Agents;

/// <summary>
/// Seeds the built-in agent definitions into the data directory on first run.
/// </summary>
public static class AgentSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void SeedBuiltInAgents(string dataBasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataBasePath);

        var builtInDir = Path.Combine(dataBasePath, "agents", "builtin");
        Directory.CreateDirectory(builtInDir);

        foreach (var agent in GetBuiltInAgents())
        {
            var path = Path.Combine(builtInDir, $"{agent.Id}.json");
            if (!File.Exists(path))
            {
                var json = JsonSerializer.Serialize(agent, JsonOptions);
                File.WriteAllText(path, json);
            }
        }
    }

    private static IEnumerable<AgentDefinition> GetBuiltInAgents()
    {
        yield return new AgentDefinition
        {
            Id = "builtin-summarizer",
            Name = "Summarizer",
            Description = "Condenses long text into concise key points and summaries.",
            SystemInstructions = """
                You are an expert summarization assistant. When given text, produce a clear,
                concise summary that captures the key points. Use bullet points for multiple
                key takeaways. Keep summaries to 3-5 bullet points unless the input is very long.
                Preserve the original meaning and tone. Output only the summary.
                """,
            Category = "Summarization",
            Icon = "📝",
            IsBuiltIn = true,
            InputDescription = "Long text, articles, documents, or any content to summarize",
            OutputDescription = "Concise bullet-point summary of key points"
        };

        yield return new AgentDefinition
        {
            Id = "builtin-translator",
            Name = "Translator",
            Description = "Translates text between languages with natural phrasing.",
            SystemInstructions = """
                You are a professional translator. Translate the input text to the target
                language specified. If no target language is specified, translate to English.
                Preserve the tone, style, and meaning of the original text. Use natural phrasing
                in the target language. If the text contains technical or domain-specific terms,
                translate them appropriately. Output only the translated text.
                """,
            Category = "Translation",
            Icon = "🌐",
            IsBuiltIn = true,
            InputDescription = "Text to translate, optionally prefixed with target language",
            OutputDescription = "Translated text in the target language"
        };

        yield return new AgentDefinition
        {
            Id = "builtin-sentiment-analyzer",
            Name = "Sentiment Analyzer",
            Description = "Analyzes text sentiment and returns classification with confidence.",
            SystemInstructions = """
                You are a sentiment analysis expert. Analyze the given text and determine its
                overall sentiment. Return your analysis in the following format:
                Sentiment: [Positive/Negative/Neutral/Mixed]
                Confidence: [High/Medium/Low]
                Key Indicators: [list the words or phrases that indicate the sentiment]
                Brief Explanation: [one sentence explaining your classification]
                """,
            Category = "Analysis",
            Icon = "📊",
            IsBuiltIn = true,
            InputDescription = "Any text to analyze for sentiment (reviews, feedback, messages)",
            OutputDescription = "Sentiment classification with confidence and explanation"
        };

        yield return new AgentDefinition
        {
            Id = "builtin-code-reviewer",
            Name = "Code Reviewer",
            Description = "Reviews code for bugs, style issues, and suggests improvements.",
            SystemInstructions = """
                You are a senior software engineer performing code review. Analyze the provided
                code and report:
                1. **Bugs**: Any logical errors, potential null references, or runtime issues
                2. **Style**: Code style improvements and best practices
                3. **Performance**: Any performance concerns or optimizations
                4. **Security**: Potential security vulnerabilities
                5. **Suggestions**: Concrete improvement suggestions with code examples
                Be constructive and specific. Rate the overall code quality from 1-10.
                """,
            Category = "Development",
            Icon = "🔍",
            IsBuiltIn = true,
            InputDescription = "Code snippets or files to review",
            OutputDescription = "Detailed code review with categorized findings and suggestions"
        };

        yield return new AgentDefinition
        {
            Id = "builtin-data-extractor",
            Name = "Data Extractor",
            Description = "Extracts structured data (entities, dates, numbers) from text.",
            SystemInstructions = """
                You are a data extraction specialist. Given unstructured text, extract and
                organize the following structured information:
                - **People**: Names and roles/relationships
                - **Organizations**: Company or institution names
                - **Dates**: Any dates or time references
                - **Locations**: Places, addresses, geographic references
                - **Numbers**: Monetary values, quantities, percentages
                - **Key Facts**: Important factual statements
                Format the output as a clean, organized list grouped by category.
                Only include categories that have extracted data.
                """,
            Category = "Extraction",
            Icon = "🗂️",
            IsBuiltIn = true,
            InputDescription = "Unstructured text containing data to extract",
            OutputDescription = "Structured data organized by category"
        };

        yield return new AgentDefinition
        {
            Id = "builtin-content-writer",
            Name = "Content Writer",
            Description = "Generates professional content from prompts and outlines.",
            SystemInstructions = """
                You are a professional content writer. Generate well-structured, engaging content
                based on the provided prompt or outline. Adapt your writing style based on 
                context:
                - For emails: professional, concise, with clear call-to-action
                - For reports: formal, data-driven, well-organized with sections
                - For articles: engaging, informative, with a clear narrative flow
                - For social media: catchy, concise, with appropriate tone
                If the type of content isn't specified, default to a professional article style.
                Ensure the content is original, well-structured, and ready to use.
                """,
            Category = "Writing",
            Icon = "✍️",
            IsBuiltIn = true,
            InputDescription = "Writing prompt, outline, or brief describing desired content",
            OutputDescription = "Polished, ready-to-use content matching the requested style"
        };

        yield return new AgentDefinition
        {
            Id = "builtin-planner",
            Name = "Planner",
            Description = "Decomposes complex goals into a structured execution plan with agent assignments.",
            SystemInstructions = """
                You are a planning and orchestration agent. Given a high-level goal or task,
                decompose it into a numbered list of concrete steps. For each step, suggest
                which type of agent should handle it.

                Output your plan inside <<<PLAN>>> and <<<END_PLAN>>> markers using this format:
                <<<PLAN>>>
                1. [Step Title] | [agent_hint: agent_name_or_category]: Detailed instruction for this step
                2. [Step Title] | [agent_hint: agent_name_or_category]: Detailed instruction for this step
                <<<END_PLAN>>>

                Rules:
                - Each step should be self-contained and actionable
                - Use descriptive agent hints (e.g., "summarizer", "code-reviewer", "content-writer")
                - Keep steps ordered logically — later steps may depend on earlier ones
                - Include 2-8 steps for most tasks
                - After the plan block, provide a brief summary of the overall approach
                """,
            Category = "Planning",
            Icon = "🗺️",
            IsBuiltIn = true,
            AgentType = "planner",
            InputDescription = "A high-level goal or complex task to decompose into steps",
            OutputDescription = "Structured execution plan with numbered steps and agent assignments"
        };
    }
}
