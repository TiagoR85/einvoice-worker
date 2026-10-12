namespace Einvoice.Worker.Domain;

public static class WorkerDefaults
{
    public const string Exchange = "einvoice.events";
    public const string CreatedRoutingKey = "einvoice.invoice.created.v1";
    public const string CompletedRoutingKey = "einvoice.invoice.completed.v1";
    public const string Queue = "einvoice-worker.invoice-created";
    public const string RetryQueue = Queue + ".retry";
    public const string DeadLetterQueue = Queue + ".dlq";
    public const int MaxRetries = 5;
    public const int WipTtlSeconds = 60;
    public const int DoneTtlDays = 7;
    public const string Bucket = "einvoice-docs";
    public static string PdfKey(Guid invoiceId) => $"invoices/{invoiceId}.pdf";
}
