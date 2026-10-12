using System.Text.Json;
using Einvoice.Worker.Contracts;
using Einvoice.Worker.Domain;

namespace Einvoice.Worker.Api;

public sealed class InvoiceApiClient : IInvoiceApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly OAuthTokenProvider _tokens;

    public InvoiceApiClient(HttpClient http, OAuthTokenProvider tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public Task<InvoiceResponse> GetInvoiceAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<InvoiceResponse>($"/v1/invoices/{id}", ct);

    public Task<PartyResponse> GetPartyAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<PartyResponse>($"/v1/parties/{id}", ct);

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        var token = await _tokens.GetTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new RetryableProcessingException($"Network error calling {path}.", ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (status is 401 or 403)
                throw new PermanentProcessingException($"{path} returned {status}.");
            if (status == 404)
                throw new RetryableProcessingException($"{path} returned 404.");
            if (status >= 500)
                throw new RetryableProcessingException($"{path} returned {status}.");
            if (!response.IsSuccessStatusCode)
                throw new PermanentProcessingException($"{path} returned {status}.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, Json, ct);
            return value ?? throw new RetryableProcessingException($"{path} returned an empty body.");
        }
    }
}
