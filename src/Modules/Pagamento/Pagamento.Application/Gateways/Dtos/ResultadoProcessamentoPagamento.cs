namespace Pagamento.Application.Gateways.Dtos;

/// <summary>
/// Resposta do processador de pagamento: identificador da cobrança no processador, se foi aprovada
/// e o detalhe do status (ex.: o motivo da recusa).
/// </summary>
public sealed record ResultadoProcessamentoPagamento(
    long PagamentoExternoId,
    bool Aprovado,
    string DetalheStatus);
