using StackExchange.Redis;

namespace Einvoice.Worker.Idempotency;

public sealed class RedisProcessingMarker(IDatabase db) : IProcessingMarker
{
    private static string DoneKey(Guid id) => $"done:invoice:{id}";
    private static string WipKey(Guid id) => $"wip:invoice:{id}";

    public async Task<bool> IsCompletedAsync(Guid invoiceId, CancellationToken ct = default) =>
        await db.KeyExistsAsync(DoneKey(invoiceId));

    public async Task<bool> TryBeginAsync(Guid invoiceId, string eventId, TimeSpan ttl, CancellationToken ct = default)
    {
        if (await db.KeyExistsAsync(DoneKey(invoiceId)))
            return false;

        return await db.StringSetAsync(WipKey(invoiceId), eventId, ttl, When.NotExists);
    }

    public async Task MarkCompletedAsync(Guid invoiceId, string eventId, TimeSpan ttl, CancellationToken ct = default)
    {
        await db.StringSetAsync(DoneKey(invoiceId), eventId, ttl, When.Always);
        await db.KeyDeleteAsync(WipKey(invoiceId));
    }

    public async Task ReleaseAsync(Guid invoiceId, CancellationToken ct = default) =>
        await db.KeyDeleteAsync(WipKey(invoiceId));
}
