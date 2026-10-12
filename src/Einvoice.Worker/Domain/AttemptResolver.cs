using System.Text;

namespace Einvoice.Worker.Domain;

public static class AttemptResolver
{
    public const string AttemptHeader = "x-attempt";

    public static int Resolve(IReadOnlyDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(AttemptHeader, out var raw))
            return 0;

        return AsInt(raw);
    }

    private static int AsInt(object? value) => value switch
    {
        null => 0,
        string s => int.TryParse(s, out var i) ? i : 0,
        byte[] bytes => int.TryParse(Encoding.UTF8.GetString(bytes), out var i) ? i : 0,
        int i => i,
        long l => (int)l,
        _ => 0
    };
}
