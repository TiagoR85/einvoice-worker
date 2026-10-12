using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Einvoice.Worker.Storage;

public sealed class MinioDocumentStore : IDocumentStore
{
    private readonly IMinioClient _client;
    private readonly string _bucket;

    public MinioDocumentStore(string endpoint, string accessKey, string secretKey, string bucket)
    {
        _bucket = bucket;
        _client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(false)
            .Build();
    }

    public async Task EnsureBucketAsync(CancellationToken ct = default)
    {
        var exists = await _client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_bucket), ct);
        if (!exists)
            await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct);
    }

    public async Task PutPdfAsync(string objectKey, byte[] content, CancellationToken ct = default)
    {
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(objectKey)
            .WithStreamData(new MemoryStream(content))
            .WithObjectSize(content.Length)
            .WithContentType("application/pdf"), ct);
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken ct = default)
    {
        try
        {
            await _client.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey), ct);
            return true;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
    }

    public async Task<byte[]?> GetAsync(string objectKey, CancellationToken ct = default)
    {
        try
        {
            var buffer = new MemoryStream();
            await _client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey)
                .WithCallbackStream(stream => stream.CopyTo(buffer)), ct);
            return buffer.ToArray();
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    private static bool IsNotFound(Exception ex) =>
        ex is ObjectNotFoundException or BucketNotFoundException ||
        ex.GetType().Name.Contains("NotFound") ||
        ex.Message.Contains("NotFound", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Not Found", StringComparison.OrdinalIgnoreCase);
}
