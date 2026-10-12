using Einvoice.Worker.Contracts;

namespace Einvoice.Worker.Api;

public interface IInvoiceApiClient
{
    Task<InvoiceResponse> GetInvoiceAsync(Guid id, CancellationToken ct = default);
    Task<PartyResponse> GetPartyAsync(Guid id, CancellationToken ct = default);
}
