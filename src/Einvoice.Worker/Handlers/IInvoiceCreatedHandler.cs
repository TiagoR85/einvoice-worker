using Einvoice.Worker.Messaging;

namespace Einvoice.Worker.Handlers;

public enum HandleResult
{
    Success,
    Retryable,
    Permanent
}

public interface IInvoiceCreatedHandler
{
    Task<HandleResult> HandleAsync(CloudEventEnvelope envelope, int attempt, CancellationToken ct);
}
