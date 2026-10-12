using System.Text.Json;
using Einvoice.Worker.Domain;

namespace Einvoice.Worker.Api;

public sealed class OAuthTokenProvider(HttpClient http, OAuthClientOptions options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        if (IsFresh())
            return _token!;

        await _gate.WaitAsync(ct);
        try
        {
            if (IsFresh())
                return _token!;

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
                ["scope"] = options.Scope
            });

            using var response = await http.PostAsync(options.TokenUrl, content, ct);
            if (!response.IsSuccessStatusCode)
                throw new PermanentProcessingException(
                    $"Token endpoint returned {(int)response.StatusCode}.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var token = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new PermanentProcessingException("Token response has no access_token.");
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp)
                ? exp.GetInt32()
                : 3600;

            _token = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 30));
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsFresh() => _token is not null && DateTimeOffset.UtcNow < _expiresAt;
}
