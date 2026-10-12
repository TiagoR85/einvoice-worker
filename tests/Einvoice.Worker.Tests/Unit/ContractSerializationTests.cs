using System.Text.Json;
using Einvoice.Worker.Contracts;

namespace Einvoice.Worker.Tests.Unit;

public sealed class ContractSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void InvoiceCreatedData_deserializes_from_api_payload_camelcase()
    {
        var json = """{"invoiceId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","number":"INV-2026-001","currency":"EUR","totalAmount":123.45}""";

        var data = JsonSerializer.Deserialize<InvoiceCreatedData>(json, Web);

        Assert.NotNull(data);
        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), data.InvoiceId);
        Assert.Equal("INV-2026-001", data.Number);
        Assert.Equal("EUR", data.Currency);
        Assert.Equal(123.45m, data.TotalAmount);
    }

    [Fact]
    public void InvoiceResponse_deserializes_dates_as_strings()
    {
        var json = """
            {"id":"3fa85f64-5717-4562-b3fc-2c963f66afa6","number":"INV-2026-001",
             "issueDate":"2026-10-11","dueDate":"2026-11-11","currency":"EUR","status":"Issued",
             "sellerId":"11111111-1111-1111-1111-111111111111",
             "buyerId":"22222222-2222-2222-2222-222222222222",
             "lines":[{"id":"44444444-4444-4444-4444-444444444444","description":"Consultoria",
                       "quantity":2,"unitPrice":100,"vatRate":0.23,"total":246}],
             "totalAmount":246}
            """;

        var invoice = JsonSerializer.Deserialize<InvoiceResponse>(json, Web);

        Assert.NotNull(invoice);
        Assert.Equal("2026-10-11", invoice.IssueDate);
        Assert.Equal(DateOnly.Parse("2026-11-11"), DateOnly.Parse(invoice.DueDate));
        Assert.Single(invoice.Lines);
        Assert.Equal(0.23m, invoice.Lines[0].VatRate);
    }

    [Fact]
    public void InvoiceCompletedData_serializes_camelcase()
    {
        var payload = new InvoiceCompletedData(
            Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            "invoices/3fa85f64-5717-4562-b3fc-2c963f66afa6.pdf",
            "application/pdf", 4096, DateTimeOffset.Parse("2026-10-11T12:00:00+00:00"));

        var json = JsonSerializer.Serialize(payload, Web);

        Assert.Contains("\"invoiceId\":", json);
        Assert.Contains("\"objectKey\":", json);
        Assert.DoesNotContain("InvoiceId", json);
    }
}
