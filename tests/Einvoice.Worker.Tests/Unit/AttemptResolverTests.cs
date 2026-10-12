using Einvoice.Worker.Domain;

namespace Einvoice.Worker.Tests.Unit;

public sealed class AttemptResolverTests
{
    private const string RetryQueue = "einvoice-worker.invoice-created.retry";

    [Fact]
    public void No_headers_means_first_delivery()
    {
        Assert.Equal(0, AttemptResolver.Resolve(null, RetryQueue));
        Assert.Equal(0, AttemptResolver.Resolve(new Dictionary<string, object?>(), RetryQueue));
    }

    [Fact]
    public void Counts_entries_for_the_retry_queue()
    {
        var xDeath = new List<object>
        {
            new Dictionary<string, object?> { ["queue"] = RetryQueue, ["count"] = 3L },
            new Dictionary<string, object?> { ["queue"] = "other-queue", ["count"] = 9L },
        };
        var headers = new Dictionary<string, object?> { ["x-death"] = xDeath };

        Assert.Equal(3, AttemptResolver.Resolve(headers, RetryQueue));
    }

    [Fact]
    public void Handles_byte_array_queue_names_and_int_counts()
    {
        var xDeath = new List<object>
        {
            new Dictionary<string, object?> { ["queue"] = System.Text.Encoding.UTF8.GetBytes(RetryQueue), ["count"] = 2 },
        };
        var headers = new Dictionary<string, object?> { ["x-death"] = xDeath };

        Assert.Equal(2, AttemptResolver.Resolve(headers, RetryQueue));
    }

    [Fact]
    public void Malformed_x_death_falls_back_to_zero()
    {
        var headers = new Dictionary<string, object?> { ["x-death"] = "not-a-list" };
        Assert.Equal(0, AttemptResolver.Resolve(headers, RetryQueue));
    }
}
