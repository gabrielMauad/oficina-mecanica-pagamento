namespace Pagamento.Application.Gateways.Dtos;

/// <summary>Dados enviados ao processador de pagamento para cobrar um orçamento.</summary>
public sealed record SolicitacaoPagamento(
    decimal Valor,
    Guid OrcamentoId,
    string Descricao,
    string TitularCartao);
