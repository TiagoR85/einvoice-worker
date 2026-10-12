namespace Einvoice.Worker.Idempotency;

public interface IProcessingMarker
{
    Task<bool> IsCompletedAsync(Guid invoiceId, CancellationToken ct = default);
    Task<bool> TryBeginAsync(Guid invoiceId, string eventId, TimeSpan ttl, CancellationToken ct = default);
    Task MarkCompletedAsync(Guid invoiceId, string eventId, TimeSpan ttl, CancellationToken ct = default);
    Task ReleaseAsync(Guid invoiceId, CancellationToken ct = default);
}
