using Einvoice.Worker.Idempotency;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Einvoice.Worker.Tests.Integration;

public sealed class RedisProcessingMarkerTests : IAsyncLifetime
{
    private RedisContainer _container = null!;
    private ConnectionMultiplexer _redis = null!;
    private RedisProcessingMarker _sut = null!;

    public async Task InitializeAsync()
    {
        _container = new RedisBuilder("redis:7-alpine").Build();
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        _sut = new RedisProcessingMarker(_redis.GetDatabase());
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task TryBegin_is_atomic_second_call_fails()
    {
        var id = Guid.NewGuid();

        Assert.True(await _sut.TryBeginAsync(id, "event-1", TimeSpan.FromSeconds(60)));
        Assert.False(await _sut.TryBeginAsync(id, "event-2", TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public async Task Release_allows_begin_again()
    {
        var id = Guid.NewGuid();

        Assert.True(await _sut.TryBeginAsync(id, "event-1", TimeSpan.FromSeconds(60)));
        await _sut.ReleaseAsync(id);
        Assert.True(await _sut.TryBeginAsync(id, "event-2", TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public async Task MarkCompleted_sets_done_and_clears_wip()
    {
        var id = Guid.NewGuid();
        await _sut.TryBeginAsync(id, "event-1", TimeSpan.FromSeconds(60));

        await _sut.MarkCompletedAsync(id, "event-1", TimeSpan.FromDays(7));

        Assert.True(await _sut.IsCompletedAsync(id));
        Assert.False(await _sut.TryBeginAsync(id, "event-2", TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public async Task IsCompleted_false_when_unknown()
    {
        Assert.False(await _sut.IsCompletedAsync(Guid.NewGuid()));
    }
}
