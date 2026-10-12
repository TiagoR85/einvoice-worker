using Einvoice.Worker.Storage;
using Testcontainers.Minio;

namespace Einvoice.Worker.Tests.Integration;

public sealed class MinioDocumentStoreTests : IAsyncLifetime
{
    private const int MinioPort = 9000;
    private MinioContainer _container = null!;
    private MinioDocumentStore _sut = null!;

    public async Task InitializeAsync()
    {
        _container = new MinioBuilder("pgsty/silo:RELEASE.2026-09-16T00-00-00Z")
            .WithUsername("minioadmin")
            .WithPassword("minioadmin")
            .Build();
        await _container.StartAsync();
        var endpoint = $"{_container.Hostname}:{_container.GetMappedPublicPort(MinioPort)}";
        _sut = new MinioDocumentStore(endpoint, _container.GetAccessKey(), _container.GetSecretKey(), WorkerDefaultsBucket());
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private static string WorkerDefaultsBucket() => "einvoice-docs";

    [Fact]
    public async Task Put_then_exists_and_get_roundtrip()
    {
        await _sut.EnsureBucketAsync();
        var payload = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }; // "%PDF-1"

        await _sut.PutPdfAsync("invoices/unit-test.pdf", payload);

        Assert.True(await _sut.ExistsAsync("invoices/unit-test.pdf"));
        var roundtrip = await _sut.GetAsync("invoices/unit-test.pdf");
        Assert.NotNull(roundtrip);
        Assert.Equal(payload, roundtrip);
    }

    [Fact]
    public async Task Exists_false_for_missing_object()
    {
        await _sut.EnsureBucketAsync();

        Assert.False(await _sut.ExistsAsync("invoices/does-not-exist.pdf"));
        Assert.Null(await _sut.GetAsync("invoices/does-not-exist.pdf"));
    }

    [Fact]
    public async Task EnsureBucket_is_idempotent()
    {
        await _sut.EnsureBucketAsync();
        await _sut.EnsureBucketAsync();

        Assert.True(await _sut.ExistsAsync("invoices/none.pdf") is false);
    }
}
