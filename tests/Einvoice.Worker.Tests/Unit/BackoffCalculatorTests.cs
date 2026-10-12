using Einvoice.Worker.Domain;

namespace Einvoice.Worker.Tests.Unit;

public sealed class BackoffCalculatorTests
{
    private readonly BackoffCalculator _sut = new();

    [Theory]
    [InlineData(0, 15_000)]
    [InlineData(1, 30_000)]
    [InlineData(2, 60_000)]
    [InlineData(3, 120_000)]
    [InlineData(4, 240_000)]
    public void Delay_grows_exponentially(int attempt, int expectedMs)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), _sut.Delay(attempt));
    }

    [Fact]
    public void Delay_is_capped_at_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), _sut.Delay(10));
    }

    [Fact]
    public void Negative_attempt_treated_as_first()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), _sut.Delay(-3));
    }
}
