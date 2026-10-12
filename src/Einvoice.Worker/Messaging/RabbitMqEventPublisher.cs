using System.Text.Json.Nodes;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;
using Einvoice.Worker.Domain;
using RabbitMQ.Client;

namespace Einvoice.Worker.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(string routingKey, string payloadJson, Guid messageId, CancellationToken ct = default);
}

public sealed class RabbitMqEventPublisher : IEventPublisher
{
    private readonly IChannel _channel;
    private readonly JsonEventFormatter _formatter = new();

    public RabbitMqEventPublisher(IConnection connection)
    {
        _channel = connection.CreateChannelAsync(new CreateChannelOptions(
                publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true))
            .GetAwaiter().GetResult();
        _channel.ExchangeDeclareAsync(WorkerDefaults.Exchange, ExchangeType.Topic, durable: true)
            .GetAwaiter().GetResult();
    }

    public async Task PublishAsync(string routingKey, string payloadJson, Guid messageId, CancellationToken ct = default)
    {
        var cloudEvent = new CloudEvent
        {
            Id = messageId.ToString(),
            Type = routingKey,
            Source = new Uri("urn:einvoice-worker"),
            Subject = routingKey,
            Time = DateTimeOffset.UtcNow,
            DataContentType = "application/json",
            Data = JsonNode.Parse(payloadJson)
        };

        var body = _formatter.EncodeStructuredModeMessage(cloudEvent, out var contentType);
        var properties = new BasicProperties { ContentType = contentType.ToString() };

        await _channel.BasicPublishAsync(
            exchange: WorkerDefaults.Exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: ct);
    }
}
