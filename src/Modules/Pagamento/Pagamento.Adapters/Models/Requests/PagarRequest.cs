namespace Pagamento.Adapters.Models.Requests;

/// <param name="TitularCartao">
/// Nome do titular do cartão. Segue a convenção dos cartões de teste do Mercado Pago:
/// <c>APRO</c> aprova o pagamento e qualquer outro valor (ex.: <c>OTHE</c>) o recusa.
/// </param>
public sealed record PagarRequest(string TitularCartao);
