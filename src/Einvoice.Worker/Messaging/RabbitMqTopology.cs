using Einvoice.Worker.Domain;
using RabbitMQ.Client;

namespace Einvoice.Worker.Messaging;

public static class RabbitMqTopology
{
    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(
            exchange: WorkerDefaults.Exchange, type: ExchangeType.Topic, durable: true,
            autoDelete: false, cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: WorkerDefaults.Queue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);

        var retryArgs = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = WorkerDefaults.Exchange,
            ["x-dead-letter-routing-key"] = WorkerDefaults.CreatedRoutingKey
        };
        await channel.QueueDeclareAsync(
            queue: WorkerDefaults.RetryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: retryArgs, cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: WorkerDefaults.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: WorkerDefaults.Queue, exchange: WorkerDefaults.Exchange,
            routingKey: WorkerDefaults.CreatedRoutingKey, cancellationToken: ct);
    }
}
