namespace Einvoice.Worker.Api;

public sealed record OAuthClientOptions(
    string TokenUrl,
    string ClientId,
    string ClientSecret,
    string Scope);
