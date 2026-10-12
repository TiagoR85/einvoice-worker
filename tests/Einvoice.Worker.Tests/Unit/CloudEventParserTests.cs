using Einvoice.Worker.Domain;
using Einvoice.Worker.Messaging;

namespace Einvoice.Worker.Tests.Unit;

public sealed class CloudEventParserTests
{
    private static byte[] Bytes(string json) => System.Text.Encoding.UTF8.GetBytes(json);

    private const string Valid = """
        {"specversion":"1.0","id":"b3fc2c96","type":"einvoice.invoice.created.v1",
         "source":"urn:einvoice-api","datacontenttype":"application/json",
         "data":{"invoiceId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","number":"INV-1","currency":"EUR","totalAmount":10}}
        """;

    [Fact]
    public void Parse_valid_structured_event()
    {
        var envelope = CloudEventParser.Parse(Bytes(Valid));

        Assert.Equal("b3fc2c96", envelope.Id);
        Assert.Equal("einvoice.invoice.created.v1", envelope.Type);
        var data = CloudEventParser.ToInvoiceCreated(envelope);
        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), data.InvoiceId);
    }

    [Fact]
    public void Parse_garbage_is_permanent()
    {
        Assert.Throws<PermanentProcessingException>(() => CloudEventParser.Parse(Bytes("not-json")));
    }

    [Fact]
    public void Missing_id_is_permanent()
    {
        Assert.Throws<PermanentProcessingException>(() =>
            CloudEventParser.Parse(Bytes("""{"specversion":"1.0","type":"x","source":"urn:x","data":{}}""")));
    }

    [Fact]
    public void Wrong_type_is_permanent()
    {
        var envelope = CloudEventParser.Parse(Bytes("""
            {"specversion":"1.0","id":"1","type":"other.event.v1","source":"urn:x","data":{}}
            """));

        Assert.Throws<PermanentProcessingException>(() => CloudEventParser.ToInvoiceCreated(envelope));
    }

    [Fact]
    public void Empty_invoice_id_is_permanent()
    {
        var envelope = CloudEventParser.Parse(Bytes("""
            {"specversion":"1.0","id":"1","type":"einvoice.invoice.created.v1","source":"urn:x",
             "data":{"invoiceId":"00000000-0000-0000-0000-000000000000","number":"x","currency":"EUR","totalAmount":1}}
            """));

        Assert.Throws<PermanentProcessingException>(() => CloudEventParser.ToInvoiceCreated(envelope));
    }
}
