using System.Text.Json.Serialization;

namespace Api.MercadoPagoMock;

// Contratos HTTP do mock do Mercado Pago (nomes em snake_case, como na API do Mercado Pago).
// São tipos próprios do mock: não compartilham código com o cliente HTTP do serviço, que consome
// o mock como consumiria o Mercado Pago real.

internal sealed record MockTokenRequest(
    [property: JsonPropertyName("client_id")] string? ClientId,
    [property: JsonPropertyName("client_secret")] string? ClientSecret,
    [property: JsonPropertyName("grant_type")] string? GrantType);

internal sealed record MockTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

internal sealed record MockPaymentRequest(
    [property: JsonPropertyName("transaction_amount")] decimal TransactionAmount,
    [property: JsonPropertyName("external_reference")] string? ExternalReference,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("card_holder_name")] string? CardHolderName);

internal sealed record MockPaymentResponse(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("status_detail")] string StatusDetail);
