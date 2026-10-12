using Einvoice.Worker.Contracts;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;

namespace Einvoice.Worker.Pdf;

public sealed record InvoiceContext(InvoiceResponse Invoice, PartyResponse Seller, PartyResponse Buyer);

public interface IPdfInvoiceGenerator
{
    byte[] Generate(InvoiceContext context);
}

public sealed class PdfInvoiceGenerator : IPdfInvoiceGenerator
{
    public byte[] Generate(InvoiceContext context)
    {
        var invoice = context.Invoice;

        using var stream = new MemoryStream();
        var pdfDoc = new PdfDocument(new PdfWriter(stream));
        using var doc = new Document(pdfDoc);

        var bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

        doc.Add(new Paragraph("INVOICE").SetFont(bold).SetFontSize(20));
        doc.Add(new Paragraph($"Number: {invoice.Number}"));
        doc.Add(new Paragraph($"Issue date: {invoice.IssueDate}    Due date: {invoice.DueDate}    Currency: {invoice.Currency}"));
        doc.Add(new Paragraph($"Status: {invoice.Status}").SetMarginTop(4));

        doc.Add(new Paragraph("Supplier").SetFont(bold).SetMarginTop(10));
        doc.Add(new Paragraph(Describe(context.Seller)));

        doc.Add(new Paragraph("Customer").SetFont(bold).SetMarginTop(6));
        doc.Add(new Paragraph(Describe(context.Buyer)));

        var table = new Table(new float[] { 3f, 1f, 1.5f, 1f, 1.5f }).UseAllAvailableWidth();
        foreach (var header in new[] { "Description", "Qty", "Unit price", "VAT %", "Total" })
            table.AddHeaderCell(new Cell().Add(new Paragraph(header).SetFont(bold)));

        foreach (var line in invoice.Lines)
        {
            table.AddCell(line.Description);
            table.AddCell(line.Quantity.ToString("0.##"));
            table.AddCell(line.UnitPrice.ToString("0.00"));
            table.AddCell((line.VatRate * 100).ToString("0.##"));
            table.AddCell(line.Total.ToString("0.00"));
        }

        doc.Add(table.SetMarginTop(10));
        doc.Add(new Paragraph($"TOTAL: {invoice.TotalAmount:0.00} {invoice.Currency}")
            .SetFont(bold).SetFontSize(14).SetMarginTop(10));

        doc.Close();
        return stream.ToArray();
    }

    private static string Describe(PartyResponse party) =>
        party.VatNumber is null
            ? $"{party.Name} - {party.Country}"
            : $"{party.Name} - {party.Country} - VAT {party.VatNumber}";
}
