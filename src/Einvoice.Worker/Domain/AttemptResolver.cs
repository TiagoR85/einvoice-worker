using System.Text;

namespace Einvoice.Worker.Domain;

public static class AttemptResolver
{
    public static int Resolve(IReadOnlyDictionary<string, object?>? headers, string retryQueueName)
    {
        if (headers is null || !headers.TryGetValue("x-death", out var raw))
            return 0;

        if (raw is not IReadOnlyList<object> entries)
            return raw is not List<object> list ? 0 : CountIn(list, retryQueueName);

        return CountIn(entries, retryQueueName);
    }

    private static int CountIn(IReadOnlyList<object> entries, string retryQueueName)
    {
        foreach (var entry in entries)
        {
            if (entry is not IReadOnlyDictionary<string, object?> dict)
                continue;

            if (!dict.TryGetValue("queue", out var queueRaw) || AsString(queueRaw) != retryQueueName)
                continue;

            if (dict.TryGetValue("count", out var countRaw) &&
                long.TryParse(AsString(countRaw), out var count))
                return (int)count;
        }

        return 0;
    }

    private static string? AsString(object? value) => value switch
    {
        null => null,
        string s => s,
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        _ => value.ToString()
    };
}
