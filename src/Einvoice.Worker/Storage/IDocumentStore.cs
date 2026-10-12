namespace Einvoice.Worker.Storage;

public interface IDocumentStore
{
    Task EnsureBucketAsync(CancellationToken ct = default);
    Task PutPdfAsync(string objectKey, byte[] content, CancellationToken ct = default);
    Task<bool> ExistsAsync(string objectKey, CancellationToken ct = default);
    Task<byte[]?> GetAsync(string objectKey, CancellationToken ct = default);
}
