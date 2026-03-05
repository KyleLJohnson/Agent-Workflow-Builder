using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using AgentWorkflowBuilder.Agents;
using AgentWorkflowBuilder.Core.Models;
using WorkflowFactory = Microsoft.Agents.AI.Workflows.AgentWorkflowBuilder;

var endpoint = "https://aoai-poc-klj.openai.azure.com/";
var deployment = "gpt-4.1-mini";

Console.WriteLine($"Endpoint: {endpoint}");
Console.WriteLine($"Deployment: {deployment}");
Console.WriteLine();

// Step 1: Direct ChatClient test - does the underlying API work?
Console.WriteLine("=== Step 1: Direct ChatClient.CompleteChatAsync test ===");
try
{
    var openAIClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
    var chatClient = openAIClient.GetChatClient(deployment);
    var directResult = await chatClient.CompleteChatAsync(
        new List<OpenAI.Chat.ChatMessage> { new OpenAI.Chat.UserChatMessage("Say hello in one word.") });
    Console.WriteLine($"Direct response: {directResult.Value.Content[0].Text}");
    Console.WriteLine($"Model: {directResult.Value.Model}");
    Console.WriteLine(">>> DIRECT API WORKS <<<");
}
catch (Exception ex)
{
    Console.WriteLine($"Direct chat FAILED: {ex.GetType().Name}: {ex.Message}");
}

// Step 2: Use AgentFactory to create agent (same as in the real app)
Console.WriteLine();
Console.WriteLine("=== Step 2: Non-streaming RunAsync via AgentFactory ===");
try
{
    var factory = new AgentFactory(endpoint, deployment);
    var agentDef = new AgentDefinition
    {
        Id = "test-agent",
        Name = "TestReviewer",
        Description = "Test code reviewer",
        SystemInstructions = "You are a helpful code reviewer. Say 'Hello from reviewer!' and nothing else."
    };
    var agent = factory.CreateAgent(agentDef);
    Console.WriteLine($"Agent created: name={agent.Name}, type={agent.GetType().Name}");
    
    var workflow = WorkflowFactory.BuildSequential([agent]);
    Console.WriteLine($"Workflow built. Type: {workflow.GetType().Name}");
    
    // Inspect executors
    foreach (var (key, binding) in workflow.ReflectExecutors())
    {
        Console.WriteLine($"  Executor: key={key}, binding-type={binding.GetType().Name}");
        if (binding is AIAgentBinding agentBinding)
        {
            Console.WriteLine($"    Options null? {agentBinding.Options is null}");
            Console.WriteLine($"    Agent name: {agentBinding.Agent?.Name}");
            Console.WriteLine($"    Agent type: {agentBinding.Agent?.GetType().Name}");
        }
    }
    
    Console.WriteLine("Calling RunAsync...");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var run = await InProcessExecution.RunAsync(workflow, "Write hello world in C#", cancellationToken: default);
    sw.Stop();
    Console.WriteLine($"RunAsync completed in {sw.ElapsedMilliseconds}ms");
    var events = run.NewEvents.ToList();
    Console.WriteLine($"NewEvents count: {events.Count}");
    
    var eventsByType = events.GroupBy(e => e.GetType().Name).Select(g => $"{g.Key}: {g.Count()}");
    Console.WriteLine($"Event breakdown: {string.Join(", ", eventsByType)}");
    
    // Show all events with data
    foreach (var evt in events)
    {
        if (evt is AgentResponseEvent are)
        {
            Console.WriteLine($"  [AgentResponse] Text length: {are.Response?.Text?.Length}");
            Console.WriteLine($"  [AgentResponse] Preview: {are.Response?.Text?[..Math.Min(200, are.Response.Text.Length)]}");
        }
        else if (evt is AgentResponseUpdateEvent arue)
        {
            Console.WriteLine($"  [AgentResponseUpdate] Text: {arue.Update?.Text}");
        }
        else if (evt is WorkflowOutputEvent woe)
        {
            var dataStr = woe.Data?.ToString() ?? "(null)";
            Console.WriteLine($"  [WorkflowOutput] Data: {dataStr[..Math.Min(200, dataStr.Length)]}");
        }
        else if (evt is ExecutorCompletedEvent ece)
        {
            Console.WriteLine($"  [ExecutorCompleted] executorId={ece.ExecutorId}, Data type={ece.Data?.GetType().Name ?? "null"}");
            if (ece.Data is AgentResponse arData)
                Console.WriteLine($"    AgentResponse.Text: {arData.Text?[..Math.Min(200, arData.Text.Length)]}");
            else if (ece.Data is not null)
                Console.WriteLine($"    Data: {ece.Data.ToString()?[..Math.Min(200, ece.Data.ToString()!.Length)]}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"RunAsync FAILED: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

// Step 3: Streaming test
Console.WriteLine();
Console.WriteLine("=== Step 3: Streaming OpenStreamingAsync ===");
try
{
    var factory2 = new AgentFactory(endpoint, deployment);
    var agentDef2 = new AgentDefinition
    {
        Id = "test-agent-2",
        Name = "TestReviewer2",
        Description = "Test code reviewer 2",
        SystemInstructions = "You are a helpful code reviewer. Say 'Hello from reviewer!' and nothing else."
    };
    var agent2 = factory2.CreateAgent(agentDef2);
    var workflow2 = WorkflowFactory.BuildSequential([agent2]);
    
    // Enable EmitAgentResponseEvents
    foreach (var (key, binding) in workflow2.ReflectExecutors())
    {
        if (binding is AIAgentBinding { Options: not null } agentBinding)
        {
            agentBinding.Options.EmitAgentResponseEvents = true;
            Console.WriteLine($"  Enabled EmitAgentResponseEvents for {key}");
        }
    }
    
    Console.WriteLine("Opening streaming run...");
    var sw2 = System.Diagnostics.Stopwatch.StartNew();
    var streamRun = await InProcessExecution.OpenStreamingAsync(workflow2, cancellationToken: default);
    var sent = await streamRun.TrySendMessageAsync("Write hello world in C#");
    Console.WriteLine($"TrySendMessageAsync returned: {sent}");
    
    var eventCount = 0;
    await foreach (var evt in streamRun.WatchStreamAsync(default))
    {
        eventCount++;
        Console.WriteLine($"  [{eventCount}] Event type: {evt.GetType().Name}");
        Console.WriteLine($"       Data type: {evt.Data?.GetType().Name ?? "null"}");
        if (evt.Data is not null)
        {
            var dataStr = evt.Data.ToString() ?? "(null)";
            Console.WriteLine($"       Data: {dataStr[..Math.Min(300, dataStr.Length)]}");
        }
        else
        {
            Console.WriteLine($"       Data: (null)");
        }
        if (evt is AgentResponseEvent are2)
        {
            Console.WriteLine($"       [AgentResponse] Text: {are2.Response?.Text}");
        }
    }
    sw2.Stop();
    Console.WriteLine($"Streaming completed in {sw2.ElapsedMilliseconds}ms. Total events: {eventCount}");
}
catch (Exception ex)
{
    Console.WriteLine($"Streaming FAILED: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
