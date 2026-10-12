namespace Einvoice.Worker.Domain;

public sealed class BackoffCalculator
{
    public const int DefaultRetryBaseMs = 15_000;
    public const int DefaultRetryCapMs = 300_000;

    public TimeSpan Delay(int attempt)
    {
        if (attempt < 0)
            attempt = 0;

        var ms = DefaultRetryBaseMs * Math.Pow(2, attempt);
        return ms >= DefaultRetryCapMs
            ? TimeSpan.FromMilliseconds(DefaultRetryCapMs)
            : TimeSpan.FromMilliseconds(ms);
    }
}
