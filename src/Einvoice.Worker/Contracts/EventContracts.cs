namespace Einvoice.Worker.Contracts;

public sealed record InvoiceCreatedData(
    Guid InvoiceId,
    string Number,
    string Currency,
    decimal TotalAmount);

public sealed record InvoiceCompletedData(
    Guid InvoiceId,
    string ObjectKey,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CompletedAt);

public sealed record InvoiceLineResponse(
    Guid Id,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal Total);

public sealed record InvoiceResponse(
    Guid Id,
    string Number,
    string IssueDate,
    string DueDate,
    string Currency,
    string Status,
    Guid SellerId,
    Guid BuyerId,
    List<InvoiceLineResponse> Lines,
    decimal TotalAmount);

public sealed record PartyResponse(
    Guid Id,
    string Name,
    string Country,
    string? VatNumber,
    string Type);
