using System.Text.Json;
using Einvoice.Worker.Contracts;
using Einvoice.Worker.Domain;

namespace Einvoice.Worker.Messaging;

public sealed record CloudEventEnvelope(string Id, string Type, JsonElement Data);

public static class CloudEventParser
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static CloudEventEnvelope Parse(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (string.IsNullOrEmpty(type))
                throw new PermanentProcessingException("CloudEvent has no 'type'.");

            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrEmpty(id))
                throw new PermanentProcessingException("CloudEvent has no 'id'.");

            if (!root.TryGetProperty("data", out var dataEl))
                throw new PermanentProcessingException("CloudEvent has no 'data'.");

            return new CloudEventEnvelope(id, type, dataEl.Clone());
        }
        catch (JsonException ex)
        {
            throw new PermanentProcessingException("Body is not valid CloudEvents JSON.", ex);
        }
    }

    public static InvoiceCreatedData ToInvoiceCreated(CloudEventEnvelope envelope)
    {
        if (envelope.Type != WorkerDefaults.CreatedRoutingKey)
            throw new PermanentProcessingException($"Unexpected event type '{envelope.Type}'.");

        var data = envelope.Data.Deserialize<InvoiceCreatedData>(Web);
        if (data is null || data.InvoiceId == Guid.Empty)
            throw new PermanentProcessingException("Event data has no invoiceId.");

        return data;
    }
}
