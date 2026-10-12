using System.Text;
using Einvoice.Worker.Domain;
using Einvoice.Worker.Handlers;
using Einvoice.Worker.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Einvoice.Worker.Tests.Integration;

public sealed class EventSubscriberTests : IAsyncLifetime
{
    private const int AmqpPort = 5672;
    private RabbitMqContainer _container = null!;
    private IConnection _connection = null!;
    private IChannel _channel = null!;

    public async Task InitializeAsync()
    {
        _container = new RabbitMqBuilder("rabbitmq:4-alpine")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();
        await _container.StartAsync();
        _connection = await new ConnectionFactory
        {
            HostName = _container.Hostname,
            Port = _container.GetMappedPublicPort(AmqpPort),
            UserName = "guest",
            Password = "guest"
        }.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();
        await RabbitMqTopology.DeclareAsync(_channel);
    }

    public async Task DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task<EventSubscriber> StartSubscriberAsync(IInvoiceCreatedHandler handler)
    {
        var subscriber = new EventSubscriber(
            _connection, handler, new BackoffCalculator(baseMs: 60, capMs: 300),
            NullLogger<EventSubscriber>.Instance);
        await subscriber.StartAsync(CancellationToken.None);
        return subscriber;
    }

    private async Task PublishAsync(string body)
    {
        var props = new BasicProperties { ContentType = "application/cloudevents+json" };
        await _channel.BasicPublishAsync(WorkerDefaults.Exchange, WorkerDefaults.CreatedRoutingKey,
            mandatory: false, basicProperties: props, body: Encoding.UTF8.GetBytes(body));
    }

    private async Task<string?> GetFromAsync(string queue, uint timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var result = await _channel.BasicGetAsync(queue, autoAck: true);
            if (result is not null)
                return Encoding.UTF8.GetString(result.Body.ToArray());
            await Task.Delay(50);
        }
        return null;
    }

    private static string ValidEvent() => """
        {"specversion":"1.0","id":"evt-1","type":"einvoice.invoice.created.v1","source":"urn:einvoice-api",
         "data":{"invoiceId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","number":"INV-1","currency":"EUR","totalAmount":10}}
        """;

    private sealed class ScriptedHandler : IInvoiceCreatedHandler
    {
        public Func<int, HandleResult> Script { get; init; } = _ => HandleResult.Success;
        public int Calls;

        public Task<HandleResult> HandleAsync(CloudEventEnvelope envelope, int attempt, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Script(attempt));
        }
    }

    [Fact]
    public async Task Success_acks_and_leaves_queue_empty()
    {
        var handler = new ScriptedHandler();
        var subscriber = await StartSubscriberAsync(handler);

        await PublishAsync(ValidEvent());
        await WaitUntilAsync(() => handler.Calls >= 1, 10_000);

        Assert.Equal(1, handler.Calls);
        await Task.Delay(200);
        Assert.Null(await _channel.BasicGetAsync(WorkerDefaults.Queue, autoAck: true));
        await subscriber.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Malformed_event_goes_straight_to_dlq()
    {
        var handler = new ScriptedHandler();
        var subscriber = await StartSubscriberAsync(handler);

        await PublishAsync("this is not json");

        var dead = await GetFromAsync(WorkerDefaults.DeadLetterQueue, 10_000);
        Assert.NotNull(dead);
        Assert.Equal(0, handler.Calls);
        await subscriber.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Retryable_failures_retry_with_backoff_then_park_in_dlq()
    {
        var handler = new ScriptedHandler { Script = _ => HandleResult.Retryable };
        var subscriber = await StartSubscriberAsync(handler);

        await PublishAsync(ValidEvent());

        var dead = await GetFromAsync(WorkerDefaults.DeadLetterQueue, 30_000);
        Assert.NotNull(dead);
        Assert.Equal(WorkerDefaults.MaxRetries + 1, handler.Calls); // attempts 0..5
        await subscriber.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Recovery_after_failure_processes_next_message()
    {
        var fail = true;
        var handler = new ScriptedHandler
        {
            Script = _ => fail ? HandleResult.Retryable : HandleResult.Success
        };
        var subscriber = await StartSubscriberAsync(handler);

        await PublishAsync(ValidEvent());
        var dead = await GetFromAsync(WorkerDefaults.DeadLetterQueue, 30_000);
        Assert.NotNull(dead);

        fail = false;
        await PublishAsync(ValidEvent().Replace("evt-1", "evt-2"));
        await WaitUntilAsync(() => handler.Calls >= WorkerDefaults.MaxRetries + 2, 10_000);

        await subscriber.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition not reached in time.");
    }
}
