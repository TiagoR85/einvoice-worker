namespace Einvoice.Worker.Domain;

public sealed class BackoffCalculator
{
    public const int DefaultRetryBaseMs = 15_000;
    public const int DefaultRetryCapMs = 300_000;

    private readonly int _baseMs;
    private readonly int _capMs;

    public BackoffCalculator(int baseMs = DefaultRetryBaseMs, int capMs = DefaultRetryCapMs)
    {
        _baseMs = baseMs;
        _capMs = capMs;
    }

    public TimeSpan Delay(int attempt)
    {
        if (attempt < 0)
            attempt = 0;

        var ms = _baseMs * Math.Pow(2, attempt);
        return ms >= _capMs
            ? TimeSpan.FromMilliseconds(_capMs)
            : TimeSpan.FromMilliseconds(ms);
    }
}
