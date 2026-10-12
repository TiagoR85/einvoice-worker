using System.Text;
using Einvoice.Worker.Domain;

namespace Einvoice.Worker.Tests.Unit;

public sealed class AttemptResolverTests
{
    [Fact]
    public void No_headers_means_first_delivery()
    {
        Assert.Equal(0, AttemptResolver.Resolve(null));
        Assert.Equal(0, AttemptResolver.Resolve(new Dictionary<string, object?>()));
    }

    [Fact]
    public void Reads_integer_attempt()
    {
        var headers = new Dictionary<string, object?> { [AttemptResolver.AttemptHeader] = 3 };
        Assert.Equal(3, AttemptResolver.Resolve(headers));
    }

    [Fact]
    public void Reads_long_attempt()
    {
        var headers = new Dictionary<string, object?> { [AttemptResolver.AttemptHeader] = 5L };
        Assert.Equal(5, AttemptResolver.Resolve(headers));
    }

    [Fact]
    public void Reads_string_attempt()
    {
        var headers = new Dictionary<string, object?> { [AttemptResolver.AttemptHeader] = "2" };
        Assert.Equal(2, AttemptResolver.Resolve(headers));
    }

    [Fact]
    public void Reads_byte_array_attempt()
    {
        var headers = new Dictionary<string, object?>
        {
            [AttemptResolver.AttemptHeader] = Encoding.UTF8.GetBytes("4")
        };
        Assert.Equal(4, AttemptResolver.Resolve(headers));
    }

    [Fact]
    public void Malformed_attempt_falls_back_to_zero()
    {
        var headers = new Dictionary<string, object?> { [AttemptResolver.AttemptHeader] = "not-a-number" };
        Assert.Equal(0, AttemptResolver.Resolve(headers));
    }
}
