using System.Text.Json;
using AgentWorkflowBuilder.Core.Interfaces;
using AgentWorkflowBuilder.Core.Models;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace AgentWorkflowBuilder.Persistence;

/// <summary>
/// Azure Service Bus implementation of <see cref="IExecutionQueue"/>.
/// Uses a queue for execution requests and a topic for cancellation signals.
/// </summary>
public sealed class ServiceBusExecutionQueue : IExecutionQueue, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _executionSender;
    private readonly ServiceBusSender _cancellationSender;
    private readonly ILogger<ServiceBusExecutionQueue> _logger;

    public ServiceBusExecutionQueue(
        ServiceBusClient client,
        string executionQueueName,
        string cancellationQueueName,
        ILogger<ServiceBusExecutionQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionQueueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancellationQueueName);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _executionSender = client.CreateSender(executionQueueName);
        _cancellationSender = client.CreateSender(cancellationQueueName);
        _logger = logger;
    }

    public async Task EnqueueAsync(ExecutionMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        string body = JsonSerializer.Serialize(message);
        ServiceBusMessage sbMessage = new(body)
        {
            MessageId = message.ExecutionId,
            SessionId = message.UserId,
            Subject = "workflow-execution",
            ContentType = "application/json"
        };

        await _executionSender.SendMessageAsync(sbMessage, ct);
        _logger.LogInformation("Enqueued execution {ExecutionId} for workflow {WorkflowId}",
            message.ExecutionId, message.WorkflowId);
    }

    public async Task EnqueueCancellationAsync(string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);

        ServiceBusMessage sbMessage = new(executionId)
        {
            MessageId = $"cancel-{executionId}",
            Subject = "execution-cancellation",
            ContentType = "text/plain"
        };

        await _cancellationSender.SendMessageAsync(sbMessage, ct);
        _logger.LogInformation("Enqueued cancellation for execution {ExecutionId}", executionId);
    }

    public async ValueTask DisposeAsync()
    {
        await _executionSender.DisposeAsync();
        await _cancellationSender.DisposeAsync();
    }
}
