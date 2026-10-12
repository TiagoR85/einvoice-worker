using Einvoice.Worker.Contracts;
using Einvoice.Worker.Pdf;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace Einvoice.Worker.Tests.Unit;

public sealed class PdfInvoiceGeneratorTests
{
    private static InvoiceContext Sample(params (string desc, decimal qty, decimal price, decimal vat, decimal total)[] lines)
    {
        var invoiceLines = lines.Select(l =>
            new InvoiceLineResponse(Guid.NewGuid(), l.desc, l.qty, l.price, l.vat, l.total)).ToList();
        var invoice = new InvoiceResponse(
            Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), "INV-2026-001",
            "2026-10-11", "2026-11-11", "EUR", "Issued",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            invoiceLines,
            invoiceLines.Sum(l => l.Total));
        return new InvoiceContext(
            invoice,
            new PartyResponse(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Acme Lda", "PT", "PT123", "Supplier"),
            new PartyResponse(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Globex SA", "ES", null, "Customer"));
    }

    [Fact]
    public void Generate_produces_pdf_with_all_key_fields()
    {
        var bytes = new PdfInvoiceGenerator().Generate(
            Sample(("Consultoria", 2, 100, 0.23m, 246)));

        Assert.True(bytes.Length > 500);
        Assert.Equal(0x25, bytes[0]); // '%'

        var text = ExtractText(bytes);
        Assert.Contains("INV-2026-001", text);
        Assert.Contains("Acme Lda", text);
        Assert.Contains("Globex SA", text);
        Assert.Contains("Consultoria", text);
        Assert.Contains("246", text);
        Assert.Contains("2026-10-11", text);
    }

    [Fact]
    public void Generate_includes_every_line()
    {
        var bytes = new PdfInvoiceGenerator().Generate(
            Sample(("Design", 1, 50, 0.23m, 50), ("Suporte", 3, 20, 0.23m, 60)));

        var text = ExtractText(bytes);
        Assert.Contains("Design", text);
        Assert.Contains("Suporte", text);
    }

    private static string ExtractText(byte[] bytes)
    {
        using var reader = new PdfReader(new MemoryStream(bytes));
        using var pdf = new PdfDocument(reader);
        return PdfTextExtractor.GetTextFromPage(pdf.GetPage(1));
    }
}
