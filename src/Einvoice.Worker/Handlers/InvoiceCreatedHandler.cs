using System.Text.Json;
using Einvoice.Worker.Api;
using Einvoice.Worker.Contracts;
using Einvoice.Worker.Domain;
using Einvoice.Worker.Idempotency;
using Einvoice.Worker.Messaging;
using Einvoice.Worker.Pdf;
using Einvoice.Worker.Storage;
using Microsoft.Extensions.Logging;

namespace Einvoice.Worker.Handlers;

public sealed class InvoiceCreatedHandler(
    IInvoiceApiClient api,
    IPdfInvoiceGenerator pdf,
    IDocumentStore store,
    IEventPublisher publisher,
    IProcessingMarker marker,
    ILogger<InvoiceCreatedHandler> logger) : IInvoiceCreatedHandler
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public async Task<HandleResult> HandleAsync(CloudEventEnvelope envelope, int attempt, CancellationToken ct)
    {
        var created = CloudEventParser.ToInvoiceCreated(envelope);
        var invoiceId = created.InvoiceId;

        if (await marker.IsCompletedAsync(invoiceId, ct))
        {
            logger.LogInformation(
                "Invoice {InvoiceId} already completed; skipping duplicate event {EventId}",
                invoiceId, envelope.Id);
            return HandleResult.Success;
        }

        if (!await marker.TryBeginAsync(invoiceId, envelope.Id,
                TimeSpan.FromSeconds(WorkerDefaults.WipTtlSeconds), ct))
        {
            logger.LogWarning(
                "Invoice {InvoiceId} has an active wip marker; deferring event {EventId}",
                invoiceId, envelope.Id);
            return HandleResult.Retryable;
        }

        try
        {
            var invoice = await api.GetInvoiceAsync(invoiceId, ct);
            var (seller, buyer) = await FetchPartiesAsync(invoice, ct);

            var bytes = pdf.Generate(new InvoiceContext(invoice, seller, buyer));

            var objectKey = WorkerDefaults.PdfKey(invoiceId);
            await store.EnsureBucketAsync(ct);
            await store.PutPdfAsync(objectKey, bytes, ct);

            var completed = new InvoiceCompletedData(
                invoiceId, objectKey, "application/pdf", bytes.Length, DateTimeOffset.UtcNow);
            await publisher.PublishAsync(
                WorkerDefaults.CompletedRoutingKey,
                JsonSerializer.Serialize(completed, Web),
                Guid.NewGuid(),
                ct);

            await marker.MarkCompletedAsync(invoiceId, envelope.Id,
                TimeSpan.FromDays(WorkerDefaults.DoneTtlDays), ct);

            logger.LogInformation("Invoice {InvoiceId} processed into {ObjectKey}", invoiceId, objectKey);
            return HandleResult.Success;
        }
        catch
        {
            await TryReleaseAsync(invoiceId, ct);
            throw;
        }
    }

    private async Task<(PartyResponse Seller, PartyResponse Buyer)> FetchPartiesAsync(
        InvoiceResponse invoice, CancellationToken ct)
    {
        var sellerTask = api.GetPartyAsync(invoice.SellerId, ct);
        var buyerTask = api.GetPartyAsync(invoice.BuyerId, ct);
        await Task.WhenAll(sellerTask, buyerTask);
        return (await sellerTask, await buyerTask);
    }

    private async Task TryReleaseAsync(Guid invoiceId, CancellationToken ct)
    {
        try
        {
            await marker.ReleaseAsync(invoiceId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to release wip marker for invoice {InvoiceId}", invoiceId);
        }
    }
}
