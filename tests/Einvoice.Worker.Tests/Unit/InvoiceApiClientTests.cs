using System.Net;
using Einvoice.Worker.Api;
using Einvoice.Worker.Domain;
using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Einvoice.Worker.Tests.Unit;

public sealed class InvoiceApiClientTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly HttpClient _http;
    private readonly InvoiceApiClient _sut;
    private readonly OAuthTokenProvider _tokens;

    public InvoiceApiClientTests()
    {
        _http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        _tokens = new OAuthTokenProvider(_http,
            new OAuthClientOptions("/connect/token", "einvoice-worker", "secret", "invoice:read"));
        _sut = new InvoiceApiClient(_http, _tokens);
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        _http.Dispose();
    }

    private void StubToken() =>
        _server.Given(Request.Create().WithPath("/connect/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new { access_token = "test-token", expires_in = 3600, token_type = "Bearer" }));

    private void StubInvoice(HttpStatusCode status, string? body = null)
    {
        var response = Response.Create().WithStatusCode((int)status);
        if (body is not null)
            response = response.WithBody(body).WithHeader("Content-Type", "application/json");
        _server.Given(Request.Create().WithPath("/v1/invoices/3fa85f64-5717-4562-b3fc-2c963f66afa6").UsingGet())
            .RespondWith(response);
    }

    [Fact]
    public async Task GetInvoice_parses_response_and_requests_token_once()
    {
        StubToken();
        StubInvoice(HttpStatusCode.OK, """
            {"id":"3fa85f64-5717-4562-b3fc-2c963f66afa6","number":"INV-2026-001",
             "issueDate":"2026-10-11","dueDate":"2026-11-11","currency":"EUR","status":"Issued",
             "sellerId":"11111111-1111-1111-1111-111111111111",
             "buyerId":"22222222-2222-2222-2222-222222222222",
             "lines":[],"totalAmount":100}
            """);

        var first = await _sut.GetInvoiceAsync(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"));
        var second = await _sut.GetInvoiceAsync(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"));

        Assert.Equal("INV-2026-001", first.Number);
        Assert.Equal(first.Id, second.Id);
        var tokenCalls = _server.LogEntries.Count(e =>
            e.RequestMessage.Path is "/connect/token" && e.RequestMessage.Method == "POST");
        Assert.Equal(1, tokenCalls);
    }

    [Fact]
    public async Task GetInvoice_404_is_retryable()
    {
        StubToken();
        StubInvoice(HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RetryableProcessingException>(() =>
            _sut.GetInvoiceAsync(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6")));
    }

    [Fact]
    public async Task GetInvoice_500_is_retryable()
    {
        StubToken();
        StubInvoice(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RetryableProcessingException>(() =>
            _sut.GetInvoiceAsync(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6")));
    }

    [Fact]
    public async Task GetInvoice_401_is_permanent()
    {
        StubToken();
        StubInvoice(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<PermanentProcessingException>(() =>
            _sut.GetInvoiceAsync(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6")));
    }

    [Fact]
    public async Task Token_failure_is_permanent()
    {
        _server.Given(Request.Create().WithPath("/connect/token").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401));

        await Assert.ThrowsAsync<PermanentProcessingException>(() => _tokens.GetTokenAsync());
    }

    [Fact]
    public async Task GetParty_parses_response()
    {
        StubToken();
        _server.Given(Request.Create().WithPath("/v1/parties/11111111-1111-1111-1111-111111111111").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("""{"id":"11111111-1111-1111-1111-111111111111","name":"Acme Lda","country":"PT","vatNumber":"PT123","type":"Supplier"}""")
                .WithHeader("Content-Type", "application/json"));

        var party = await _sut.GetPartyAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        Assert.Equal("Acme Lda", party.Name);
        Assert.Equal("PT", party.Country);
    }
}
