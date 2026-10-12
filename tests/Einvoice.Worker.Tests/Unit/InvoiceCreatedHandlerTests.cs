using System.Text.Json;
using Einvoice.Worker.Api;
using Einvoice.Worker.Contracts;
using Einvoice.Worker.Domain;
using Einvoice.Worker.Handlers;
using Einvoice.Worker.Idempotency;
using Einvoice.Worker.Messaging;
using Einvoice.Worker.Pdf;
using Einvoice.Worker.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einvoice.Worker.Tests.Unit;

public sealed class InvoiceCreatedHandlerTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly Guid _invoiceId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

    private CloudEventEnvelope Envelope(string? eventId = "evt-1") => CloudEventParser.Parse(
        System.Text.Encoding.UTF8.GetBytes($$$"""
            {"specversion":"1.0","id":"{{{eventId}}}","type":"{{{WorkerDefaults.CreatedRoutingKey}}}","source":"urn:einvoice-api",
             "data":{"invoiceId":"{{{_invoiceId}}}","number":"INV-1","currency":"EUR","totalAmount":10}}
            """));

    private sealed class FakeApi : IInvoiceApiClient
    {
        public int InvoiceCalls;
        public Func<Guid, Exception?>? FailWith;

        public Task<InvoiceResponse> GetInvoiceAsync(Guid id, CancellationToken ct = default)
        {
            InvoiceCalls++;
            if (FailWith?.Invoke(id) is { } ex)
                throw ex;
            return Task.FromResult(new InvoiceResponse(id, "INV-1", "2026-10-11", "2026-11-11", "EUR",
                "Issued", Guid.NewGuid(), Guid.NewGuid(),
                [new InvoiceLineResponse(Guid.NewGuid(), "Consultoria", 1, 10, 0.23m, 10)], 10m));
        }

        public Task<PartyResponse> GetPartyAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(new PartyResponse(id, "Acme Lda", "PT", null, "Supplier"));
    }

    private sealed class FakeMarker : IProcessingMarker
    {
        public readonly Dictionary<Guid, string> Done = new();
        public readonly Dictionary<Guid, string> Wip = new();

        public Task<bool> IsCompletedAsync(Guid invoiceId, CancellationToken ct = default) =>
            Task.FromResult(Done.ContainsKey(invoiceId));

        public Task<bool> TryBeginAsync(Guid invoiceId, string eventId, TimeSpan ttl, CancellationToken ct = default)
        {
            if (Wip.ContainsKey(invoiceId))
                return Task.FromResult(false);
            Wip[invoiceId] = eventId;
            return Task.FromResult(true);
        }

        public Task MarkCompletedAsync(Guid invoiceId, string eventId, TimeSpan ttl, CancellationToken ct = default)
        {
            Done[invoiceId] = eventId;
            Wip.Remove(invoiceId);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid invoiceId, CancellationToken ct = default)
        {
            Wip.Remove(invoiceId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IDocumentStore
    {
        public Task EnsureBucketAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task PutPdfAsync(string objectKey, byte[] content, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string objectKey, CancellationToken ct = default) => Task.FromResult(true);
        public Task<byte[]?> GetAsync(string objectKey, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
    }

    private sealed class FakePublisher : IEventPublisher
    {
        public readonly List<(string Rk, string Payload)> Published = new();

        public Task PublishAsync(string routingKey, string payloadJson, Guid messageId, CancellationToken ct = default)
        {
            Published.Add((routingKey, payloadJson));
            return Task.CompletedTask;
        }
    }

    private (InvoiceCreatedHandler Handler, FakeApi Api, FakeMarker Marker, FakePublisher Publisher) Build()
    {
        var api = new FakeApi();
        var marker = new FakeMarker();
        var publisher = new FakePublisher();
        var handler = new InvoiceCreatedHandler(
            api, new PdfInvoiceGenerator(), new FakeStore(), publisher, marker,
            NullLogger<InvoiceCreatedHandler>.Instance);
        return (handler, api, marker, publisher);
    }

    [Fact]
    public async Task Happy_path_publishes_completed_and_marks_done()
    {
        var (handler, api, marker, publisher) = Build();

        var result = await handler.HandleAsync(Envelope(), 0, CancellationToken.None);

        Assert.Equal(HandleResult.Success, result);
        Assert.True(await marker.IsCompletedAsync(_invoiceId));
        Assert.False(marker.Wip.ContainsKey(_invoiceId));
        var published = Assert.Single(publisher.Published);
        Assert.Equal(WorkerDefaults.CompletedRoutingKey, published.Rk);
        var data = JsonSerializer.Deserialize<InvoiceCompletedData>(published.Payload, Web)!;
        Assert.Equal(_invoiceId, data.InvoiceId);
        Assert.Equal(WorkerDefaults.PdfKey(_invoiceId), data.ObjectKey);
        Assert.Equal("application/pdf", data.ContentType);
        Assert.True(data.SizeBytes > 0);
        Assert.True(api.InvoiceCalls >= 1);
    }

    [Fact]
    public async Task Already_done_short_circuits_without_api_calls()
    {
        var (handler, api, marker, publisher) = Build();
        await handler.HandleAsync(Envelope("evt-1"), 0, CancellationToken.None);

        var result = await handler.HandleAsync(Envelope("evt-2"), 0, CancellationToken.None);

        Assert.Equal(HandleResult.Success, result);
        Assert.Single(publisher.Published);
        Assert.Equal(1, api.InvoiceCalls);
    }

    [Fact]
    public async Task Live_wip_marker_returns_retryable_without_api_calls()
    {
        var (handler, api, marker, _) = Build();
        await marker.TryBeginAsync(_invoiceId, "other", TimeSpan.FromSeconds(60));

        var result = await handler.HandleAsync(Envelope(), 0, CancellationToken.None);

        Assert.Equal(HandleResult.Retryable, result);
        Assert.Equal(0, api.InvoiceCalls);
    }

    [Fact]
    public async Task Retryable_api_failure_releases_wip_and_propagates()
    {
        var (handler, api, marker, _) = Build();
        api.FailWith = _ => new RetryableProcessingException("boom");

        await Assert.ThrowsAsync<RetryableProcessingException>(() =>
            handler.HandleAsync(Envelope(), 0, CancellationToken.None));
        Assert.False(marker.Wip.ContainsKey(_invoiceId));
        Assert.False(await marker.IsCompletedAsync(_invoiceId));
    }

    [Fact]
    public async Task Permanent_api_failure_releases_wip_and_propagates()
    {
        var (handler, api, marker, _) = Build();
        api.FailWith = _ => new PermanentProcessingException("401");

        await Assert.ThrowsAsync<PermanentProcessingException>(() =>
            handler.HandleAsync(Envelope(), 0, CancellationToken.None));
        Assert.False(marker.Wip.ContainsKey(_invoiceId));
    }
}
