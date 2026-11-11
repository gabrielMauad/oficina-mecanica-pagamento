using System.Text.Json.Serialization;

namespace Pagamento.Adapters.Gateways.Http;

// Contratos HTTP do Mercado Pago consumidos pelo MercadoPagoGateway (nomes em snake_case, como na
// API do Mercado Pago): POST oauth/token e POST v1/payments. Campos extras do JSON de resposta são
// ignorados na desserialização.

internal sealed record TokenRequisicao(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_secret")] string ClientSecret,
    [property: JsonPropertyName("grant_type")] string GrantType);

internal sealed record TokenResposta(
    [property: JsonPropertyName("access_token")] string AccessToken);

internal sealed record PagamentoRequisicao(
    [property: JsonPropertyName("transaction_amount")] decimal TransactionAmount,
    [property: JsonPropertyName("external_reference")] string ExternalReference,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("card_holder_name")] string CardHolderName);

internal sealed record PagamentoResposta(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("status_detail")] string? StatusDetail);
