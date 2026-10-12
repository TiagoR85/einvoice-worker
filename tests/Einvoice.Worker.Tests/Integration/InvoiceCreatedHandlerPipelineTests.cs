using System.Text;
using Einvoice.Worker.Api;
using Einvoice.Worker.Domain;
using Einvoice.Worker.Handlers;
using Einvoice.Worker.Idempotency;
using Einvoice.Worker.Messaging;
using Einvoice.Worker.Pdf;
using Einvoice.Worker.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using StackExchange.Redis;
using Testcontainers.Minio;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Einvoice.Worker.Tests.Integration;

public sealed class InvoiceCreatedHandlerPipelineTests : IAsyncLifetime
{
    private const int AmqpPort = 5672;
    private const int MinioPort = 9000;

    private RabbitMqContainer _rabbit = null!;
    private RedisContainer _redis = null!;
    private MinioContainer _minio = null!;
    private WireMockServer _api = null!;
    private IConnection _connection = null!;
    private IChannel _channel = null!;
    private ConnectionMultiplexer _redisClient = null!;
    private string _completedQueue = null!;
    private EventSubscriber _subscriber = null!;
    private MinioDocumentStore _store = null!;
    private RedisProcessingMarker _marker = null!;

    public async Task InitializeAsync()
    {
        _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();
        _redis = new RedisBuilder("redis:7-alpine").Build();
        _minio = new MinioBuilder("pgsty/silo:RELEASE.2026-09-16T00-00-00Z")
            .WithUsername("minioadmin")
            .WithPassword("minioadmin")
            .Build();
        await Task.WhenAll(_rabbit.StartAsync(), _redis.StartAsync(), _minio.StartAsync());

        _connection = await new ConnectionFactory
        {
            HostName = _rabbit.Hostname,
            Port = _rabbit.GetMappedPublicPort(AmqpPort),
            UserName = "guest",
            Password = "guest"
        }.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();
        await RabbitMqTopology.DeclareAsync(_channel);

        _redisClient = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _marker = new RedisProcessingMarker(_redisClient.GetDatabase());

        _store = new MinioDocumentStore(
            $"{_minio.Hostname}:{_minio.GetMappedPublicPort(MinioPort)}",
            "minioadmin", "minioadmin", WorkerDefaults.Bucket);
        await _store.EnsureBucketAsync();

        _api = WireMockServer.Start();
        StubApiSuccess();

        _completedQueue = $"test-completed-{Guid.NewGuid():N}";
        await _channel.QueueDeclareAsync(_completedQueue, durable: false, exclusive: true,
            autoDelete: true, arguments: null);
        await _channel.QueueBindAsync(_completedQueue, WorkerDefaults.Exchange,
            WorkerDefaults.CompletedRoutingKey);

        var apiHttp = new HttpClient { BaseAddress = new Uri(_api.Url!) };
        var tokens = new OAuthTokenProvider(apiHttp,
            new OAuthClientOptions("/connect/token", "einvoice-worker", "secret", "invoice:read"));
        var handler = new InvoiceCreatedHandler(
            new InvoiceApiClient(apiHttp, tokens),
            new PdfInvoiceGenerator(),
            _store,
            new RabbitMqEventPublisher(_connection),
            _marker,
            NullLogger<InvoiceCreatedHandler>.Instance);

        _subscriber = new EventSubscriber(
            _connection, handler, new BackoffCalculator(baseMs: 60, capMs: 300),
            NullLogger<EventSubscriber>.Instance);
        await _subscriber.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _subscriber.StopAsync(CancellationToken.None);
        _api.Stop();
        _api.Dispose();
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
        await _redisClient.DisposeAsync();
        await Task.WhenAll(_rabbit.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask(), _minio.DisposeAsync().AsTask());
    }

    private void StubApiSuccess()
    {
        _api.Given(Request.Create().WithPath("/connect/token").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { access_token = "t", expires_in = 3600, token_type = "Bearer" }));

        _api.Given(Request.Create().WithPath("/v1/invoices/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {"id":"3fa85f64-5717-4562-b3fc-2c963f66afa6","number":"INV-2026-001","issueDate":"2026-10-11",
                     "dueDate":"2026-11-11","currency":"EUR","status":"Issued",
                     "sellerId":"11111111-1111-1111-1111-111111111111","buyerId":"22222222-2222-2222-2222-222222222222",
                     "lines":[{"id":"44444444-4444-4444-4444-444444444444","description":"Consultoria",
                               "quantity":1,"unitPrice":100,"vatRate":0.23,"total":100}],
                     "totalAmount":100}
                    """));

        _api.Given(Request.Create().WithPath("/v1/parties/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"id":"11111111-1111-1111-1111-111111111111","name":"Acme Lda","country":"PT","vatNumber":"PT123","type":"Supplier"}"""));
    }

    private async Task PublishCreatedAsync(string eventId, Guid invoiceId)
    {
        var body = $$$"""
            {"specversion":"1.0","id":"{{{eventId}}}","type":"{{{WorkerDefaults.CreatedRoutingKey}}}","source":"urn:einvoice-api",
             "data":{"invoiceId":"{{{invoiceId}}}","number":"INV-2026-001","currency":"EUR","totalAmount":100}}
            """;
        var props = new BasicProperties { ContentType = "application/cloudevents+json" };
        await _channel.BasicPublishAsync(WorkerDefaults.Exchange, WorkerDefaults.CreatedRoutingKey,
            mandatory: false, basicProperties: props, body: Encoding.UTF8.GetBytes(body));
    }

    private async Task<string?> PollCompletedAsync(int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var got = await _channel.BasicGetAsync(_completedQueue, autoAck: true);
            if (got is not null)
                return Encoding.UTF8.GetString(got.Body.ToArray());
            await Task.Delay(100);
        }
        return null;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Condition not reached in time.");
    }

    [Fact]
    public async Task Happy_path_stores_pdf_publishes_completed_and_marks_done()
    {
        var invoiceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await PublishCreatedAsync("evt-happy", invoiceId);

        var completed = await PollCompletedAsync(30_000);

        Assert.NotNull(completed);
        Assert.Contains(invoiceId.ToString(), completed);
        Assert.Contains("application/pdf", completed);
        Assert.True(await _store.ExistsAsync(WorkerDefaults.PdfKey(invoiceId)));
        var pdfBytes = await _store.GetAsync(WorkerDefaults.PdfKey(invoiceId));
        Assert.NotNull(pdfBytes);
        Assert.Equal(0x25, pdfBytes![0]);
        await WaitUntilAsync(() => _marker.IsCompletedAsync(invoiceId), 10_000);
    }

    [Fact]
    public async Task Duplicate_event_after_completion_is_skipped()
    {
        var invoiceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        await PublishCreatedAsync("evt-dup-1", invoiceId);
        Assert.NotNull(await PollCompletedAsync(30_000));

        await PublishCreatedAsync("evt-dup-2", invoiceId);
        await Task.Delay(1500);

        Assert.Null(await PollCompletedAsync(1_500));
        Assert.True(await _marker.IsCompletedAsync(invoiceId));
    }

    [Fact]
    public async Task Transient_api_failure_recovers_via_retry()
    {
        var invoiceId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        _api.Reset();
        _api.Given(Request.Create().WithPath("/connect/token").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { access_token = "t", expires_in = 3600, token_type = "Bearer" }));
        _api.Given(Request.Create().WithPath("/v1/invoices/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        await PublishCreatedAsync("evt-flaky", invoiceId);
        await WaitUntilAsync(() => Task.FromResult(
            _api.LogEntries.Any(e => e.RequestMessage!.Path == "/v1/invoices/" + invoiceId)), 15_000);

        _api.Reset();
        StubApiSuccess();

        var completed = await PollCompletedAsync(30_000);
        Assert.NotNull(completed);
    }

    [Fact]
    public async Task Permanent_token_failure_parks_message_in_dlq()
    {
        var invoiceId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        _api.Reset();
        _api.Given(Request.Create().WithPath("/connect/token").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401));

        await PublishCreatedAsync("evt-perm", invoiceId);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        string? dead = null;
        while (DateTime.UtcNow < deadline && dead is null)
        {
            var got = await _channel.BasicGetAsync(WorkerDefaults.DeadLetterQueue, autoAck: true);
            if (got is not null)
                dead = Encoding.UTF8.GetString(got.Body.ToArray());
            else
                await Task.Delay(100);
        }

        Assert.NotNull(dead);
        Assert.False(await _marker.IsCompletedAsync(invoiceId));
    }
}
